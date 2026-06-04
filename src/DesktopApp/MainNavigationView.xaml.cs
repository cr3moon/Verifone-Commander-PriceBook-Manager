// -----------------------------------------------------------------------
// <copyright file="MainNavigationView.xaml.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp
{
    using Microsoft.UI.Xaml.Controls;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels;

    public sealed partial class MainNavigationView : UserControl
    {
        public MainNavigationView()
        {
            this.InitializeComponent();
        }

        public MainNavigationVm ViewModel { get; } = App.ViewModelResolver.Resolve<MainNavigationVm>();

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            System.Type target = null;
            if (args.SelectedItem.GetType() == typeof(AccountPageVm))
            {
                target = typeof(AccountPage);
            }
            else if (args.SelectedItem.GetType() == typeof(BulkOperationsPageVm))
            {
                target = typeof(BulkOperationsPage);
            }
            else if (args.SelectedItem.GetType() == typeof(EditPageVm))
            {
                target = typeof(EditPage);
            }
            else if (args.SelectedItem.GetType() == typeof(SearchPageVm))
            {
                target = typeof(SearchPage);
            }
            else if (args.SelectedItem.GetType() == typeof(ItemsPageVm))
            {
                target = typeof(ItemsPage);
            }
            else if (args.SelectedItem.GetType() == typeof(ImportPageVm))
            {
                target = typeof(ImportPage);
            }
            else if (args.SelectedItem.GetType() == typeof(SettingsPageVm))
            {
                target = typeof(SettingsPage);
            }

            if (target == null)
            {
                return;
            }

            // The Items page is a full-height working surface: it must fill the viewport
            // and scroll its own list internally, so the filter bar and the possible-
            // duplicates side panel stay on screen while the operator pages through a
            // long catalog. Every other page keeps host-level vertical scrolling.
            this.PageScrollHost.ContentOrientation = target == typeof(ItemsPage)
                ? ScrollingContentOrientation.None
                : ScrollingContentOrientation.Vertical;

            this.NavViewFrame.Navigate(target);
        }
    }
}
