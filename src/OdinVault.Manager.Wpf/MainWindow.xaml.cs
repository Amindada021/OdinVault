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
 async void OnLoaded(object s,RoutedEventArgs e){tray=new TrayService(ShowFromTray,()=>{allowClose=true;Close();});if(settings.StartMinimizedToTray){Hide();ShowInTaskbar=false;}await vm.RefreshAsync();if(settings.CheckForUpdatesOnStart)await CheckForUpdatesOnStartAsync();}
 async Task CheckForUpdatesOnStartAsync(){try{using var updater=new GitHubUpdateService();var update=await updater.CheckAsync();if(update.IsUpdateAvailable&&settings.ShowTrayNotifications)tray?.Notify("OdinVault",$"نسخه {update.LatestVersion} آماده بروزرسانی است.");}catch{}}
 void OnClosing(object? s,CancelEventArgs e){if(!allowClose&&settings.MinimizeToTray){e.Cancel=true;Hide();ShowInTaskbar=false;if(settings.ShowTrayNotifications)tray?.Notify("OdinVault","برنامه در ناحیه اعلان‌ها در حال اجراست.");}}
 void ShowFromTray(){ShowInTaskbar=true;Show();WindowState=WindowState.Normal;Activate();}
 void ApplyTheme(){var merged=System.Windows.Application.Current.Resources.MergedDictionaries;merged[0]=new ResourceDictionary{Source=new Uri(dark?"Themes/Dark.xaml":"Themes/Light.xaml",UriKind.Relative)};}
 void ApplyLanguage(){var en=settings.Language=="en";FlowDirection=en?System.Windows.FlowDirection.LeftToRight:System.Windows.FlowDirection.RightToLeft;var merged=System.Windows.Application.Current.Resources.MergedDictionaries;var old=merged.FirstOrDefault(x=>x.Source?.OriginalString.StartsWith("Localization/Strings.",StringComparison.OrdinalIgnoreCase)==true);if(old is not null)merged.Remove(old);merged.Add(new ResourceDictionary{Source=new Uri(en?"Localization/Strings.en.xaml":"Localization/Strings.fa.xaml",UriKind.Relative)});}
 void Dashboard_Click(object sender,RoutedEventArgs e){SetActiveNav(sender);PageHost.Visibility=Visibility.Collapsed;DashboardPanel.Visibility=Visibility.Visible;}
 async void Databases_Click(object sender,RoutedEventArgs e){SetActiveNav(sender);var db=new DatabasesViewModel(vm.Client);var view=new DatabasesView{DataContext=db};PageHost.Content=view;DashboardPanel.Visibility=Visibility.Collapsed;PageHost.Visibility=Visibility.Visible;await db.LoadAsync();}
 async void Backups_Click(object sender,RoutedEventArgs e){SetActiveNav(sender);var model=new BackupsViewModel(vm.Client);PageHost.Content=new BackupsView{DataContext=model};DashboardPanel.Visibility=Visibility.Collapsed;PageHost.Visibility=Visibility.Visible;await model.LoadAsync();}
 void Update_Click(object s,RoutedEventArgs e){SetActiveNav(s);ShowPage(new UpdateView());}
 void Mobile_Click(object s,RoutedEventArgs e){SetActiveNav(s);ShowPage(new MobileView{DataContext=new MobileViewModel(vm.Client)});}
 void Settings_Click(object s,RoutedEventArgs e){SetActiveNav(s);ShowPage(new SettingsView(settingsStore,settings,ApplySettings));}
 void ApplySettings(ManagerSettings value){settings=value;dark=settings.Theme.Equals("Dark",StringComparison.OrdinalIgnoreCase);ApplyTheme();ApplyLanguage();}
 void Restore_Click(object s,RoutedEventArgs e){SetActiveNav(s);ShowPage(new RestoreView{DataContext=new RestoreViewModel(vm.Client)});}
 async void Storage_Click(object s,RoutedEventArgs e){SetActiveNav(s);var m=new StorageViewModel(vm.Client);ShowPage(new StorageView{DataContext=m});await m.LoadAsync();}
 async void Alerts_Click(object s,RoutedEventArgs e){SetActiveNav(s);var m=new AlertsViewModel(vm.Client);ShowPage(new AlertsView{DataContext=m});await m.LoadAsync();}
 async void Reports_Click(object s,RoutedEventArgs e){SetActiveNav(s);var m=new ReportsViewModel(vm.Client);ShowPage(new ReportsView{DataContext=m});await m.LoadAsync();}
 void SetActiveNav(object source){foreach(var button in FindVisualChildren<System.Windows.Controls.Button>(this).Where(x=>Equals(x.Tag,"Nav")||Equals(x.Tag,"NavActive")))button.Tag="Nav";if(source is System.Windows.Controls.Button selected)selected.Tag="NavActive";}
 static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T:DependencyObject{for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var nested in FindVisualChildren<T>(child))yield return nested;}}
 void ShowPage(object view){PageHost.Content=view;DashboardPanel.Visibility=Visibility.Collapsed;PageHost.Visibility=Visibility.Visible;}
 void Theme_Click(object sender,RoutedEventArgs e){dark=!dark;ApplyTheme();settings=settings with{Theme=dark?"Dark":"Light"};settingsStore.Save(settings);}
}
