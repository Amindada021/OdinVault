using System.Windows.Input;
namespace OdinVault.Manager.Wpf.Infrastructure;
internal sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute=null) : ICommand
{
 private bool running;
 public event EventHandler? CanExecuteChanged;
 public bool CanExecute(object? p)=>!running&&(canExecute?.Invoke()??true);
 public async void Execute(object? p){ if(!CanExecute(p)) return; running=true; CanExecuteChanged?.Invoke(this,EventArgs.Empty); try{await execute();}finally{running=false;CanExecuteChanged?.Invoke(this,EventArgs.Empty);} }
}
