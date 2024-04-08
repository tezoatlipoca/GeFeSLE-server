using System.Text.Json;
public class UserDto {
    // this is the same as the Identiy User ID
    public string? Id { get; set; }
    // this is the Identity Claims username from the session Claims
    public string? UserName { get; set; }
    // this is the highest Role that gets saved in the session Claims
    public string? Role { get; set; }

    public bool IsAuthenticated { get; set; }  = false;

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}