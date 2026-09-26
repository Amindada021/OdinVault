using System.Windows;
using OdinVault.Manager.Wpf.ViewModels;
namespace OdinVault.Manager.Wpf;
public partial class MainWindow : Window
{
 readonly MainViewModel vm=new(); bool dark;
 public MainWindow(){InitializeComponent();DataContext=vm;Loaded+=async(_,_)=>await vm.RefreshAsync();Closed+=(_,_)=>vm.Dispose();}
 void Theme_Click(object sender,RoutedEventArgs e){dark=!dark;var merged=Application.Current.Resources.MergedDictionaries;merged[0]=new ResourceDictionary{Source=new Uri(dark?"Themes/Dark.xaml":"Themes/Light.xaml",UriKind.Relative)};}
}
