using System.Windows;
using OdinVault.Manager.Wpf.ViewModels;
using OdinVault.Manager.Wpf.Views;
namespace OdinVault.Manager.Wpf;
public partial class MainWindow : Window
{
 readonly MainViewModel vm=new(); bool dark;
 public MainWindow(){InitializeComponent();DataContext=vm;Loaded+=async(_,_)=>await vm.RefreshAsync();Closed+=(_,_)=>vm.Dispose();}
 void Dashboard_Click(object sender,RoutedEventArgs e){PageHost.Visibility=Visibility.Collapsed;DashboardPanel.Visibility=Visibility.Visible;}
 async void Databases_Click(object sender,RoutedEventArgs e){var db=new DatabasesViewModel(vm.Client);var view=new DatabasesView{DataContext=db};PageHost.Content=view;DashboardPanel.Visibility=Visibility.Collapsed;PageHost.Visibility=Visibility.Visible;await db.LoadAsync();}
 async void Backups_Click(object sender,RoutedEventArgs e){var model=new BackupsViewModel(vm.Client);PageHost.Content=new BackupsView{DataContext=model};DashboardPanel.Visibility=Visibility.Collapsed;PageHost.Visibility=Visibility.Visible;await model.LoadAsync();}
 async void Storage_Click(object s,RoutedEventArgs e){var m=new StorageViewModel(vm.Client);ShowPage(new StorageView{DataContext=m});await m.LoadAsync();}
 async void Alerts_Click(object s,RoutedEventArgs e){var m=new AlertsViewModel(vm.Client);ShowPage(new AlertsView{DataContext=m});await m.LoadAsync();}
 async void Reports_Click(object s,RoutedEventArgs e){var m=new ReportsViewModel(vm.Client);ShowPage(new ReportsView{DataContext=m});await m.LoadAsync();}
 void ShowPage(object view){PageHost.Content=view;DashboardPanel.Visibility=Visibility.Collapsed;PageHost.Visibility=Visibility.Visible;}
 void Theme_Click(object sender,RoutedEventArgs e){dark=!dark;var merged=Application.Current.Resources.MergedDictionaries;merged[0]=new ResourceDictionary{Source=new Uri(dark?"Themes/Dark.xaml":"Themes/Light.xaml",UriKind.Relative)};}
}
