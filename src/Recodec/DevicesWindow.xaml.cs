using System.Windows;
using Recodec.ViewModels;

namespace Recodec;

public partial class DevicesWindow : Window
{
    public DevicesWindow(DevicesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Closed += (_, _) => vm.Cancel();
    }
}
