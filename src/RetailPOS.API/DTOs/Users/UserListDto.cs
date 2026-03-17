namespace RetailPOS.API.DTOs.Users;

public class UserListDto
{
    public IEnumerable<UserDto> Users { get; set; } = new List<UserDto>();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
