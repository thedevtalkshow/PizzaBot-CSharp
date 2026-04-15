using System.ComponentModel;

namespace PizzaBot.ConsumerWeb.Services;

/// <summary>
/// Calculates the number of pizzas to order based on headcount and appetite.
/// The method signature (name + Description attributes) is what AIFunctionFactory
/// reflects over to produce the tool schema the agent uses.
/// </summary>
public static class PizzaCalculator
{
    [Description("Calculates the number of pizzas to order based on the number of people and their appetite level.")]
    public static int CalculateNumberOfPizzasToOrder(
        [Description("The number of people we are ordering pizza for.")] int numberOfPeople,
        [Description("The appetite level: 'light' (1 slice per person), 'average' (2 slices per person), or 'heavy' (4 slices per person). Defaults to 'average'.")] string appetite = "average")
    {
        int slicesPerPerson = appetite.ToLowerInvariant() switch
        {
            "light" => 1,
            "heavy" => 4,
            _ => 2  // average or default
        };

        int slicesPerPizza = 12;
        int totalSlicesNeeded = numberOfPeople * slicesPerPerson;
        return (int)Math.Ceiling((double)totalSlicesNeeded / slicesPerPizza);
    }
}
