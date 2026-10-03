using System.Reflection;
using MergeDesk.App.Services;
using MergeDesk.Core;

internal static class AccountChecks
{
    public static int Run()
    {
        var method = typeof(ClassicOutlookService).GetMethod("ResolveSendingAccount", BindingFlags.Static | BindingFlags.NonPublic)!;
        var defaultAccount = new OutlookAccountFixture("default-store", "default@example.com");
        var intended = new OutlookAccountFixture("selected-store", "owner@example.com");
        var session = new OutlookSessionFixture(defaultAccount, intended);
        var resolved = method.Invoke(null, [session, new DraftAccount("selected-store", "Owner", "OWNER@example.com")]);
        if (!ReferenceEquals(resolved, intended)) throw new Exception("Wrong sending account selected");
        Console.WriteLine("PASS: reopens with explicitly selected live profile account instead of saved/default account");
        foreach (var selection in new[] { new DraftAccount("selected-store", "Wrong address", "different@example.com"), new DraftAccount("missing-store", "Missing", "owner@example.com") })
        {
            try { method.Invoke(null, [session, selection]); throw new Exception("Unexpected fallback to default account"); }
            catch (TargetInvocationException ex) when (ex.InnerException is SubmissionRejectedException) { Console.WriteLine("PASS: missing/mismatched profile account blocks submission without default fallback"); }
        }
        return 3;
    }
}
public sealed class OutlookSessionFixture(params OutlookAccountFixture[] accounts)
{ public OutlookAccountsFixture Accounts { get; } = new(accounts); }
public sealed class OutlookAccountsFixture(OutlookAccountFixture[] accounts)
{ public int Count => accounts.Length; public OutlookAccountFixture Item(int index) => accounts[index - 1]; }
public sealed class OutlookAccountFixture(string storeId, string email)
{ public OutlookStoreFixture DeliveryStore { get; } = new(storeId); public string SmtpAddress => email; }
public sealed class OutlookStoreFixture(string id)
{ public string StoreID => id; }
