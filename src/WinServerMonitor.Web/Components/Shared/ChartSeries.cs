namespace WinServerMonitor.Web.Components.Shared;

/// <param name="Name">Legend / tooltip label.</param>
/// <param name="Slot">Categorical color slot (1 = blue, 2 = orange, ...), see app.css --series-N.</param>
/// <param name="Values">One value per timestamp.</param>
public sealed record ChartSeries(string Name, int Slot, IReadOnlyList<double> Values);
