using System.ComponentModel;
using System.Windows;
using WPSupportTools.App.ViewModels;
using WPSupportTools.Core;

namespace WPSupportTools.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Interromper a manutenção no meio poderia deixar o serviço de impressão parado.
        if (_viewModel.IsBusy)
        {
            e.Cancel = true;
            MessageBox.Show(this, "Aguarde a conclusão da manutenção antes de fechar.", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        base.OnClosing(e);
    }
}
