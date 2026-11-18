namespace ProxyGAyer.Core;

public sealed record BatchApplyResult(ServiceDefinition Service, bool Success, string? Error = null);

// A batch reuses the normal, per-service journal. Successful sections keep their
// independent Reset behavior even if a different section cannot be configured.
public sealed class BatchProxyApplier(ChangeManager manager, ConfigPlanner planner)
{
    public IReadOnlyList<BatchApplyResult> Apply(IEnumerable<ServiceDefinition> selectedServices, ProxyProfile profile)
    {
        profile.Validate();
        var selected = selectedServices.DistinctBy(s => s.Id).ToArray();
        if (selected.Length == 0) throw new ArgumentException("حداقل یک سرویس را انتخاب کنید.");
        var incompatible = selected.Where(s => s.SingleProxy && profile.Http != profile.Https).ToArray();
        if (incompatible.Length > 0)
            throw new ArgumentException("این سرویس‌ها یک پروکسی مشترک برای HTTP و HTTPS می‌خواهند: " +
                string.Join("، ", incompatible.Select(s => s.Name)) +
                ". گزینه استفاده از همین پروکسی برای HTTPS را روشن کنید یا تیک این سرویس‌ها را بردارید. هیچ تغییری اعمال نشد.");

        var results = new List<BatchApplyResult>();
        foreach (var service in selected)
        {
            try
            {
                manager.Apply(service.Id, profile, planner.Plan(service, profile));
                results.Add(new(service, true));
            }
            catch (Exception exception)
            {
                results.Add(new(service, false, exception.Message));
            }
        }
        return results;
    }
}
