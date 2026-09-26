using System.Windows;
using OdinVault.Manager.Wpf.ViewModels;
using OdinVault.Manager.Wpf.Views;
using OdinVault.Manager.Wpf.Services;
using System.ComponentModel;
namespace OdinVault.Manager.Wpf;
public partial class MainWindow : Window
{
 readonly MainViewModel vm=new(); readonly ManagerSettingsStore settingsStore=new(); ManagerSettings settings=new(); TrayService? tray; bool dark, allowClose;
 public MainWindow(){InitializeComponent();DataContext=vm;settings=settingsStore.Load();dark=settings.Theme.Equals("Dark",StringComparison.OrdinalIgnoreCase);ApplyTheme();ApplyLanguage();Loaded+=OnLoaded;Closing+=OnClosing;Closed+=(_,_)=>{tray?.Dispose();vm.Dispose();};}
 async void OnLoaded(object s,RoutedEventArgs e){tray=new TrayService(ShowFromTray,()=>{allowClose=true;Close();});if(settings.StartMinimizedToTray){Hide();ShowInTaskbar=false;}await vm.RefreshAsync();}
 void OnClosing(object? s,CancelEventArgs e){if(!allowClose&&settings.MinimizeToTray){e.Cancel=true;Hide();ShowInTaskbar=false;tray?.Notify("OdinVault","برنامه در ناحیه اعلان‌ها در حال اجراست.");}}
 void ShowFromTray(){ShowInTaskbar=true;Show();WindowState=WindowState.Normal;Activate();}
 void ApplyTheme(){var merged=Application.Current.Resources.MergedDictionaries;merged[0]=new ResourceDictionary{Source=new Uri(dark?"Themes/Dark.xaml":"Themes/Light.xaml",UriKind.Relative)};}
 void ApplyLanguage(){FlowDirection=settings.Language=="en"?FlowDirection.LeftToRight:FlowDirection.RightToLeft;}
 void Dashboard_Click(object sender,RoutedEventArgs e){PageHost.Visibility=Visibility.Collapsed;DashboardPanel.Visibility=Visibility.Visible;}
 async void Databases_Click(object sender,RoutedEventArgs e){var db=new DatabasesViewModel(vm.Client);var view=new DatabasesView{DataContext=db};PageHost.Content=view;DashboardPanel.Visibility=Visibility.Collapsed;PageHost.Visibility=Visibility.Visible;await db.LoadAsync();}
 async void Backups_Click(object sender,RoutedEventArgs e){var model=new BackupsViewModel(vm.Client);PageHost.Content=new BackupsView{DataContext=model};DashboardPanel.Visibility=Visibility.Collapsed;PageHost.Visibility=Visibility.Visible;await model.LoadAsync();}
 void Restore_Click(object s,RoutedEventArgs e){ShowPage(new RestoreView{DataContext=new RestoreViewModel(vm.Client)});}
 async void Storage_Click(object s,RoutedEventArgs e){var m=new StorageViewModel(vm.Client);ShowPage(new StorageView{DataContext=m});await m.LoadAsync();}
 async void Alerts_Click(object s,RoutedEventArgs e){var m=new AlertsViewModel(vm.Client);ShowPage(new AlertsView{DataContext=m});await m.LoadAsync();}
 async void Reports_Click(object s,RoutedEventArgs e){var m=new ReportsViewModel(vm.Client);ShowPage(new ReportsView{DataContext=m});await m.LoadAsync();}
 void ShowPage(object view){PageHost.Content=view;DashboardPanel.Visibility=Visibility.Collapsed;PageHost.Visibility=Visibility.Visible;}
 void Theme_Click(object sender,RoutedEventArgs e){dark=!dark;ApplyTheme();settings=settings with{Theme=dark?"Dark":"Light"};settingsStore.Save(settings);}
}
