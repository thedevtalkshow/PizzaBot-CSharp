namespace Shared.Models;

public class Pizza
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = "";
    public List<string> Toppings { get; set; } = []; // IDs of default toppings
}
