using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Customer;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    /// <summary>List customers with optional search and pagination</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CustomerListDto>>> GetAll([FromQuery] CustomerSearchDto searchDto)
    {
        var result = await _customerService.SearchAsync(searchDto);
        return Ok(ApiResponse<CustomerListDto>.SuccessResponse(result));
    }

    /// <summary>Quick search customers by name/phone/email for POS autocomplete</summary>
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<IEnumerable<CustomerDto>>>> Search([FromQuery] string q)
    {
        var customers = await _customerService.QuickSearchAsync(q);
        return Ok(ApiResponse<IEnumerable<CustomerDto>>.SuccessResponse(customers));
    }

    /// <summary>Get a customer by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> GetById(long id)
    {
        var customer = await _customerService.GetByIdAsync(id);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(customer));
    }

    /// <summary>Create a new customer</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> Create([FromBody] CreateCustomerDto dto)
    {
        var customer = await _customerService.CreateAsync(dto);
        return CreatedAtAction(
            nameof(GetById),
            new { id = customer.Id },
            ApiResponse<CustomerDto>.SuccessResponse(customer, "Customer created successfully"));
    }

    /// <summary>Update an existing customer</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> Update(long id, [FromBody] UpdateCustomerDto dto)
    {
        var customer = await _customerService.UpdateAsync(id, dto);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(customer, "Customer updated successfully"));
    }

    /// <summary>Delete a customer</summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        await _customerService.DeleteAsync(id);
        return Ok(ApiResponse.SuccessResponse("Customer deleted successfully"));
    }

    /// <summary>Add loyalty points to a customer</summary>
    [HttpPost("{id}/add-points")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> AddPoints(long id, [FromQuery] int points)
    {
        var customer = await _customerService.AddLoyaltyPointsAsync(id, points);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(customer, $"{points} loyalty points added"));
    }

    /// <summary>Redeem loyalty points from a customer</summary>
    [HttpPost("{id}/redeem-points")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> RedeemPoints(long id, [FromQuery] int points)
    {
        var customer = await _customerService.RedeemLoyaltyPointsAsync(id, points);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(customer, $"{points} loyalty points redeemed"));
    }
}
