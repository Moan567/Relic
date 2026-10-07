using Engine.Input;
using Engine.Utils.Settings;
using Gum.Forms.Controls;
using MonoGameGum;
using MonoGameGum.GueDeriving;
using System;
using System.Collections.Generic;

namespace Engine.UI
{
    public class OptionsWindow
    {
        public CGWindow mainWindow;
        public bool Opened { get; set; }

        private readonly Dictionary<string, Gum.Forms.Controls.Panel> tabContents = new();
        private readonly Dictionary<string, Gum.Forms.Controls.Button> tabButtons = new();
        private string currentTab = string.Empty;

        private readonly List<KeybindOption> controlsOptions = new();

        // Layout constants
        private const int RowHeight = 32;
        private const int TopPadding = 16;

        public void Initialize()
        {
            mainWindow = new CGWindow("Options");
            mainWindow.Width = 600;
            mainWindow.Height = 500;
            mainWindow.MinWidth = 600;
            mainWindow.MinHeight = 500;
            mainWindow.Anchor(Gum.Wireframe.Anchor.Center);

            BuildAllTabs();
            BuildFooterButtons();

            // Activate the first available tab
            if (GameSettings.RegisteredTabs.Count > 0)
                SwitchTab(GameSettings.RegisteredTabs[0]);

            Opened = true;
            mainWindow.Open();
        }

        public void Open()
        {
            Opened = true;
            mainWindow.Open();
            SyncAllOptions();
        }

        public void Close()
        {
            Opened = false;
            mainWindow.Close();
        }

        private void BuildAllTabs()
        {
            int tabButtonX = 0;

            foreach (var tabName in GameSettings.RegisteredTabs)
            {
                var content = new Gum.Forms.Controls.Panel(new CGDarkPanel(35));
                content.Visual.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
                content.Visual.HeightUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
                content.Anchor(Gum.Wireframe.Anchor.Top);
                content.Width = 0;
                content.Height = -72;   // leave room for tab buttons above and footer below
                content.Y = 32;
                content.IsVisible = false;
                content.IsEnabled = false;

                PopulateTabContent(content, tabName);

                tabContents[tabName] = content;
                mainWindow.InnerPanel.AddChild(content);

                var btn = new CGButton().FormsControl;
                btn.Anchor(Gum.Wireframe.Anchor.TopLeft);
                btn.Text = tabName;
                btn.X = tabButtonX;

                // Capture loop variable for the closure
                var captured = tabName;
                btn.Click += (s, e) => SwitchTab(captured);

                tabButtons[tabName] = btn;
                mainWindow.InnerPanel.AddChild(btn);

                tabButtonX += (int)btn.Width;
            }
        }

        private void PopulateTabContent(Gum.Forms.Controls.Panel panel, string tabName)
        {
            if (tabName == OptionsTabs.Controls)
            {
                PopulateControlsTab(panel);
                return;
            }

            int y = TopPadding;
            foreach (var option in GameSettings.GetOptions(tabName))
            {
                option.Build(panel, y);
                y += RowHeight;
            }
        }
        private void PopulateControlsTab(Panel panel)
        {
            int y = TopPadding;

            var scrollPanel = new CGScrollViewer();
            scrollPanel.FormsControl.Dock(Gum.Wireframe.Dock.Fill);
            panel.AddChild(scrollPanel.FormsControl);

            var childPanel = new Panel();

            new SectionHeaderOption("General").Build(childPanel, y);
            y += RowHeight;
            foreach (var option in GameSettings.GetOptions(OptionsTab.Controls))
            {
                option.Build(childPanel, y);
                y += RowHeight;
            }
            foreach (var category in InputRegistry.GetCategories())
            {
                new SectionHeaderOption(category).Build(childPanel, y);
                y += RowHeight;

                foreach (var binding in InputRegistry.GetByCategory(category))
                {
                    var row = new KeybindOption(binding);
                    row.Build(childPanel, y);
                    controlsOptions.Add(row);
                    y += RowHeight;
                }
            }
            scrollPanel.FormsControl.AddChild(childPanel);
            childPanel.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
            childPanel.Width = 0;
        }
        private void BuildFooterButtons()
        {
            var apply = new CGButton().FormsControl;
            apply.Anchor(Gum.Wireframe.Anchor.BottomRight);
            apply.Text = "Accept";
            apply.Click += (s, e) => Close();
            mainWindow.InnerPanel.AddChild(apply);
        }

        private void SwitchTab(string tabName)
        {
            // Hide everything
            foreach (var (_, content) in tabContents)
            {
                content.IsVisible = false;
                content.IsEnabled = false;
            }

            // Show the chosen tab
            if (tabContents.TryGetValue(tabName, out var target))
            {
                target.IsVisible = true;
                target.IsEnabled = true;
            }

            currentTab = tabName;
        }

        /// <summary>
        /// Refreshes every option control to reflect the current engine state.
        /// </summary>
        private void SyncAllOptions()
        {
            foreach (var tabName in GameSettings.RegisteredTabs)
                foreach (var option in GameSettings.GetOptions(tabName))
                    option.Sync();

            foreach (var option in controlsOptions)
                option.Sync();
        }
    }
}