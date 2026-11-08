namespace Nexus.Reporting.Analytics;

public enum HealthStatus
{
    Unknown = 0,
    Green = 1,
    Amber = 2,
    Red = 3
}

/// <summary>
/// Earned-value figures for one project, worked out from its budget and its latest progress.
/// <list type="bullet">
/// <item>PV (planned value) = budget x planned%; EV (earned value) = budget x actual%.</item>
/// <item>SV = EV - PV; SPI = actual% / planned% (above 1 is ahead, below 1 behind).</item>
/// <item>AC (actual cost) comes from outside (approved invoices); CV = EV - AC; CPI = EV / AC.</item>
/// <item>EAC = budget / CPI; ETC = EAC - AC; VAC = budget - EAC.</item>
/// <item>Forecast finish = start + planned duration / SPI.</item>
/// </list>
/// A figure that cannot be worked out (no budget, nothing planned yet, no cost yet) is null - never
/// a made-up zero.
/// </summary>
public static class EarnedValueCalculator
{
    /// <summary>SPI/CPI at or above this is Green; at or above <see cref="AmberFloor"/> is Amber; below it is Red.</summary>
    public const decimal GreenFloor = 0.95m;

    public const decimal AmberFloor = 0.85m;

    public static EarnedValue Calculate(
        decimal? budget, decimal? plannedPercent, decimal? actualPercent, decimal? actualCost,
        DateOnly? start, DateOnly? end, DateOnly asOf)
    {
        decimal? pv = null, ev = null, sv = null, spi = null, cv = null, cpi = null, eac = null, etc = null, vac = null;

        if (plannedPercent is not null && actualPercent is not null)
        {
            if (plannedPercent > 0)
            {
                spi = Round(actualPercent.Value / plannedPercent.Value);
            }

            if (budget is not null)
            {
                pv = Round(budget.Value * plannedPercent.Value / 100m);
                ev = Round(budget.Value * actualPercent.Value / 100m);
                sv = ev - pv;
            }
        }

        if (ev is not null && actualCost is not null)
        {
            cv = ev - actualCost;
            if (actualCost > 0 && ev > 0)
            {
                cpi = Round(ev.Value / actualCost.Value);
                eac = Round(budget!.Value / cpi.Value);
                etc = eac - actualCost;
                vac = budget - eac;
            }
        }

        var (forecastEnd, delay) = Forecast(start, end, spi, actualPercent);
        var overdue = end is not null && asOf > end && actualPercent is < 100;
        return new EarnedValue(
            budget, pv, ev, sv, spi, actualCost, cv, cpi, eac, etc, vac, forecastEnd, delay, overdue,
            Combine(Classify(spi), Classify(cpi)));
    }

    public static HealthStatus Classify(decimal? index) => index switch
    {
        null => HealthStatus.Unknown,
        >= GreenFloor => HealthStatus.Green,
        >= AmberFloor => HealthStatus.Amber,
        _ => HealthStatus.Red
    };

    /// <summary>The worse of the two; Unknown only when neither is known.</summary>
    public static HealthStatus Combine(HealthStatus a, HealthStatus b)
    {
        if (a == HealthStatus.Unknown)
        {
            return b;
        }

        return b == HealthStatus.Unknown ? a : (HealthStatus)Math.Max((int)a, (int)b);
    }

    private static (DateOnly? End, int? DelayDays) Forecast(DateOnly? start, DateOnly? end, decimal? spi, decimal? actualPercent)
    {
        // A finished project has no forecast; one with no progress yet cannot be projected.
        if (start is null || end is null || spi is null || spi <= 0 || actualPercent is >= 100 || end < start)
        {
            return (null, null);
        }

        var plannedDays = end.Value.DayNumber - start.Value.DayNumber;
        var forecastDays = (int)Math.Ceiling(plannedDays / spi.Value);
        var forecast = start.Value.AddDays(forecastDays);
        return (forecast, forecast.DayNumber - end.Value.DayNumber);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record EarnedValue(
    decimal? BudgetAtCompletion, decimal? PlannedValue, decimal? EarnedValueAmount, decimal? ScheduleVariance, decimal? Spi,
    decimal? ActualCost, decimal? CostVariance, decimal? Cpi,
    decimal? EstimateAtCompletion, decimal? EstimateToComplete, decimal? VarianceAtCompletion,
    DateOnly? ForecastEnd, int? ForecastDelayDays, bool IsOverdue, HealthStatus Health);
