using System.Windows;
using OdinVault.Manager.Wpf.ViewModels;

namespace OdinVault.Manager.Wpf.Views;

public partial class ReplicaReceiverWindow : Window
{
    public ReplicaReceiverWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is ReplicaViewModel vm)
                await vm.LoadAsync();
        };
    }

    private async void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReplicaViewModel vm)
            await vm.SaveFolderAsync();
    }
}
