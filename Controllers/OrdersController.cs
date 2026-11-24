using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Models;
using System.Text.Json;

namespace OrderService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly OrderDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly string _productServiceUrl;

        public OrdersController(OrderDbContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _context = context;
            _httpClient = httpClientFactory.CreateClient();
            _productServiceUrl = configuration["ServiceUrls:ProductService"] ?? "http://localhost:5001";
        }

        // 1. XEM TẤT CẢ ĐƠN HÀNG: GET api/orders
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
        {
            return await _context.Orders.ToListAsync();
        }

        // 2. XEM CHI TIẾT ĐƠN HÀNG: GET api/orders/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<Order>> GetOrder(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
                return NotFound(new { message = $"Không tìm thấy đơn hàng ID = {id}" });

            return order;
        }

        // 3. TẠO ĐƠN HÀNG (Gọi sang ProductService kiểm tra số lượng): POST api/orders
        [HttpPost]
        public async Task<ActionResult<Order>> CreateOrder(int productId, int quantity)
        {
            if (quantity <= 0)
                return BadRequest(new { message = "Số lượng đặt mua phải lớn hơn 0" });

            // Gọi HTTP GET sang ProductService (cổng 5001)
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync($"{_productServiceUrl}/api/products/{productId}");
            }
            catch
            {
                return StatusCode(503, new { message = "Không thể kết nối tới ProductService (hãy chắc chắn cổng 5001 đang chạy)" });
            }

            if (!response.IsSuccessStatusCode)
                return NotFound(new { message = $"Sản phẩm ID = {productId} không tồn tại bên ProductService" });

            var json = await response.Content.ReadAsStringAsync();
            var product = JsonSerializer.Deserialize<ProductDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (product == null)
                return BadRequest(new { message = "Dữ liệu sản phẩm không hợp lệ" });

            // Kiểm tra số lượng tồn kho
            if (product.StockQuantity < quantity)
            {
                return BadRequest(new
                {
                    message = $"Không đủ hàng trong kho! Sản phẩm '{product.Name}' chỉ còn {product.StockQuantity} cái."
                });
            }

            // Tạo đơn hàng mới
            var order = new Order
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = quantity,
                TotalPrice = product.Price * quantity,
                Status = "Confirmed",
                OrderDate = DateTime.UtcNow
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
        }

        // 4. CẬP NHẬT TRẠNG THÁI ĐƠN HÀNG: PUT api/orders/{id}/status
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] string newStatus)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
                return NotFound(new { message = $"Không tìm thấy đơn hàng ID = {id}" });

            order.Status = newStatus;
            await _context.SaveChangesAsync();

            return Ok(order);
        }

        // 5. XÓA ĐƠN HÀNG: DELETE api/orders/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
                return NotFound(new { message = $"Không tìm thấy đơn hàng ID = {id}" });

            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa đơn hàng thành công" });
        }
    }
}