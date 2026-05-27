// -----------------------------------------------------------------------
// <copyright file="ItemsPage.xaml.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp
{
    using Microsoft.UI.Xaml.Controls;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels;

    public sealed partial class ItemsPage : Page
    {
        public ItemsPage()
        {
            this.InitializeComponent();
        }

        public ItemsPageVm ViewModel { get; } = App.ViewModelResolver.Resolve<ItemsPageVm>();
    }
}
