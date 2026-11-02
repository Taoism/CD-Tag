using System.Windows.Controls;
using CDTag.Common;
using CDTag.ViewModels.Rip;

namespace CDTag.Views.Rip
{
    /// <summary>Rip tab (CD-Tag 2.x layout).</summary>
    public partial class RipView : UserControl
    {
        public RipView()
        {
            InitializeComponent();
            DataContext = IoC.Resolve<RipViewModel>();
        }
    }
}
