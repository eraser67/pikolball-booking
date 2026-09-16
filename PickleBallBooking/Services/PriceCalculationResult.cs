namespace PickleBallBooking.Services;

public class PriceCalculationResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public decimal Price { get; init; }

    public static PriceCalculationResult Fail(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };

    public static PriceCalculationResult Ok(decimal price) => new() { Success = true, Price = price };
}
