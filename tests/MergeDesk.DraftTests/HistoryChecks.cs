using System.IO;
using System.Windows.Threading;
using MergeDesk.Core;
using MergeDesk.Infrastructure;
using MergeDesk.App.Services;
using MergeDesk.App.ViewModels;
internal static class HistoryChecks
{
 static int count;
 static void Check(bool ok,string detail) { if(!ok)throw new Exception("FAIL: "+detail);count++;Console.WriteLine("PASS: "+detail); }
 static void Until(Func<bool> condition) {var end=DateTime.UtcNow.AddSeconds(20);while(!condition()&&DateTime.UtcNow<end){Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Thread.Sleep(5);}if(!condition())throw new TimeoutException();}
 static void Wait(Task task) { Until(()=>task.IsCompleted); task.GetAwaiter().GetResult(); }
 public static int Run(string root)
 {
  count=0;var repo=new SqliteRunRepository(Path.Combine(root,"clear-history.db"));var file=Path.Combine(root,"kept-export.eml");File.WriteAllText(file,"saved export");
  var record=new RunRecord("clear-test",DateTimeOffset.UtcNow,file,[new(2,"alex@example.com","Test","Submitted",file,DateTimeOffset.UtcNow)]);
  Wait(repo.SaveAsync(record));var dialogs=new Dialogs();var vm=new MainViewModel(new RecipientReader(),repo,dialogs);Wait(vm.LoadHistoryAsync());vm.SelectRun(vm.History.Single());
  vm.ClearHistoryCommand.Execute(null);Check(vm.History.Count==1&&vm.Results.Count==1,"declined history confirmation keeps runs and displayed results");
  var ledger=new SqliteSendLedger(Path.Combine(root,"clear-send-ledger.db"));var reservation=ledger.ReserveAsync("already-sent","original");Wait(reservation);Wait(ledger.SaveAsync(reservation.Result.Attempt with {State="Submitted"}));
  dialogs.Confirm=true;vm.ClearHistoryCommand.Execute(null);Until(()=>!vm.IsBusy&&vm.ClearHistoryCommand.CanExecute(null));
  Check(vm.History.Count==0&&vm.Results.Count==0&&vm.OutputPath==""&&vm.Progress=="","confirmed history clearing removes stale displayed results and output path");
  Wait(vm.LoadHistoryAsync());Check(vm.History.Count==0,"history refresh stays empty after persistent deletion");
  Check(File.Exists(file),"history UI clearing preserves output files");
  var repeat=ledger.ReserveAsync("already-sent","repeat");Wait(repeat);Check(!repeat.Result.Owned&&repeat.Result.Attempt.State=="Submitted","history clearing keeps duplicate-send ledger reservations");
  var blocked=new BlockingRepository(record);var busyVm=new MainViewModel(new RecipientReader(),blocked,dialogs);Wait(busyVm.LoadHistoryAsync());busyVm.ClearHistoryCommand.Execute(null);Until(()=>blocked.Started);
  Check(busyVm.IsBusy&&!busyVm.ClearHistoryCommand.CanExecute(null)&&!busyVm.HistoryCommand.CanExecute(null),"history clearing blocks overlapping operations");
  blocked.Completion.SetException(new IOException("fixture clear failure"));Until(()=>!busyVm.IsBusy&&busyVm.ClearHistoryCommand.CanExecute(null));
  Check(busyVm.History.Count==1&&busyVm.Status.Contains("fixture clear failure"),"failed deletion retains displayed history and reports failure");
  vm.CloseConnection();busyVm.CloseConnection();return count;
 }
 sealed class Dialogs:IDesktopDialogs {public bool Confirm; public bool ConfirmHistoryClear()=>Confirm; public string? OpenSource()=>null;public string[] OpenAttachments()=>[];public string? ChooseOutput()=>null;public string? ProjectFile(bool save)=>null;}
 sealed class BlockingRepository(RunRecord record):IRunRepository {public volatile bool Started;public TaskCompletionSource Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);public Task ClearAsync(CancellationToken token=default){Started=true;return Completion.Task;}public Task SaveAsync(RunRecord run,CancellationToken token=default)=>Task.CompletedTask;public Task<IReadOnlyList<RunRecord>> LoadAsync(CancellationToken token=default)=>Task.FromResult<IReadOnlyList<RunRecord>>([record]);}
}
