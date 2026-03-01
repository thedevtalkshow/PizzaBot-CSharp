using System.ComponentModel;

public class PizzaCalculator
{
    [Description("Calculates the number of pizzas to order based on the number of people and their appetite level.")]
    public static int CalculateNumberOfPizzasToOrder(
        [Description("The number of people we are ordering pizza for.")] int numberOfPeople,
        [Description("The appetite level: 'light' (1 slice per person), 'average' (2 slices per person), or 'heavy' (4 slices per person). Defaults to 'average'.")] string appetite = "average")
    {
        // Determine slices per person based on appetite
        int slicesPerPerson = appetite.ToLower() switch
        {
            "light" => 1,
            "heavy" => 4,
            _ => 2  // average or default
        };

        Console.WriteLine($"PizzaCalculator: Calculating pizzas for {numberOfPeople} people with {appetite} appetite ({slicesPerPerson} slices/person)...");

        int slicesPerPizza = 12;
        int totalSlicesNeeded = numberOfPeople * slicesPerPerson;
        return (int)Math.Ceiling((double)totalSlicesNeeded / slicesPerPizza);
    }
}
