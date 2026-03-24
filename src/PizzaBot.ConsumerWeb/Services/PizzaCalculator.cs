namespace PizzaBot.ConsumerWeb.Services;

/// <summary>
/// Calculates the number of pizzas to order based on headcount and appetite.
/// Mirrors the function tool defined in the Foundry agent.
/// </summary>
public static class PizzaCalculator
{
    public static int Calculate(int numberOfPeople, string appetite = "average")
    {
        var multiplier = appetite.ToLowerInvariant() switch
        {
            "light" => 0.5,
            "hungry" => 1.0,
            "starving" => 1.5,
            _ => 0.75  // average
        };

        return (int)Math.Ceiling(numberOfPeople * multiplier);
    }
}
