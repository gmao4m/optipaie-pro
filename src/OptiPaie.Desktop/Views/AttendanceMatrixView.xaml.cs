using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using OptiPaie.Desktop.ViewModels.Attendance;

namespace OptiPaie.Desktop.Views
{
    /// <summary>
    /// The Attendance Matrix. The frozen identity columns and one colour column per day
    /// are generated in code (the day count changes with the month), each cell wired to
    /// the view model's paint command. The DataGrid virtualises rows and columns, so the
    /// grid stays fast for hundreds of employees.
    /// </summary>
    public partial class AttendanceMatrixView : UserControl
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private AttendanceMatrixViewModel _vm;

        public AttendanceMatrixView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.LayoutChanged -= OnLayoutChanged;
            _vm = e.NewValue as AttendanceMatrixViewModel;
            if (_vm != null)
            {
                _vm.LayoutChanged += OnLayoutChanged;
                BuildColumns();
            }
        }

        private void OnLayoutChanged(object sender, EventArgs e) => BuildColumns();

        private void BuildColumns()
        {
            if (_vm == null) return;

            Grid.Columns.Clear();
            Grid.Columns.Add(SelectColumn());
            Grid.Columns.Add(TextColumn("N°", "Number", 56));
            Grid.Columns.Add(NameColumn());
            Grid.Columns.Add(TextColumn("Département", "Department", 130));
            Grid.Columns.Add(TextColumn("Poste", "Position", 130));
            Grid.FrozenColumnCount = 5;

            for (int day = 1; day <= _vm.DayCount; day++)
            {
                Grid.Columns.Add(DayColumn(day));
            }
        }

        // -- fixed columns -----------------------------------------------------

        private static DataGridTextColumn TextColumn(string header, string path, double width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(path),
                Width = new DataGridLength(width),
                IsReadOnly = true
            };
        }

        /// <summary>Employee name column; a double-click opens the attendance history.</summary>
        private static DataGridTemplateColumn NameColumn()
        {
            const string xaml =
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "<Border Background='Transparent' Cursor='Hand' ToolTip='Double-cliquer : historique de l&apos;employé'>" +
                "<Border.InputBindings>" +
                "<MouseBinding MouseAction='LeftDoubleClick' " +
                "Command='{Binding DataContext.EmployeeDetailCommand, RelativeSource={RelativeSource AncestorType={x:Type DataGrid}}}' " +
                "CommandParameter='{Binding}' /></Border.InputBindings>" +
                "<TextBlock Text='{Binding Name}' VerticalAlignment='Center' Margin='6,0,0,0' TextTrimming='CharacterEllipsis' /></Border></DataTemplate>";

            return new DataGridTemplateColumn
            {
                Header = "Employé",
                Width = new DataGridLength(190),
                CellTemplate = (DataTemplate)XamlReader.Parse(xaml)
            };
        }

        private static DataGridTemplateColumn SelectColumn()
        {
            const string xaml =
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<CheckBox HorizontalAlignment='Center' VerticalAlignment='Center' " +
                "IsChecked='{Binding IsSelected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}' />" +
                "</DataTemplate>";

            return new DataGridTemplateColumn
            {
                Header = "✓",
                Width = new DataGridLength(34),
                CanUserResize = false,
                CellTemplate = (DataTemplate)XamlReader.Parse(xaml)
            };
        }

        // -- one colour column per day ----------------------------------------

        private DataGridTemplateColumn DayColumn(int day)
        {
            var date = new DateTime(_vm.SelectedYear, _vm.SelectedMonth, day);
            bool weekend = date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday;
            int index = day - 1;

            string tmpl =
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "<Border Background='{Binding Cells[I].Background}' ToolTip='{Binding Cells[I].Tooltip}' " +
                "BorderBrush='#E7EBF0' BorderThickness='0,0,1,0' Cursor='Hand'>" +
                "<Border.InputBindings>" +
                "<MouseBinding MouseAction='LeftClick' " +
                "Command='{Binding DataContext.PaintCellCommand, RelativeSource={RelativeSource AncestorType={x:Type DataGrid}}}' " +
                "CommandParameter='{Binding Cells[I]}' /></Border.InputBindings>" +
                "<TextBlock Text='{Binding Cells[I].Letter}' HorizontalAlignment='Center' VerticalAlignment='Center' " +
                "FontSize='10.5' FontWeight='SemiBold' Foreground='#1B2430' /></Border></DataTemplate>";
            tmpl = tmpl.Replace("Cells[I]", "Cells[" + index.ToString(CultureInfo.InvariantCulture) + "]");

            return new DataGridTemplateColumn
            {
                Header = DayHeader(day, date, weekend),
                Width = new DataGridLength(30),
                CanUserResize = false,
                CellTemplate = (DataTemplate)XamlReader.Parse(tmpl)
            };
        }

        /// <summary>Clickable day header: click fills that day for every visible employee.</summary>
        private object DayHeader(int day, DateTime date, bool weekend)
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock
            {
                Text = day.ToString(CultureInfo.InvariantCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11
            });
            panel.Children.Add(new TextBlock
            {
                Text = Fr.DateTimeFormat.GetShortestDayName(date.DayOfWeek),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 9,
                Foreground = weekend ? Brushes.Firebrick : (Brush)new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA2))
            });

            var button = new Button
            {
                Content = panel,
                Command = _vm.PaintDayCommand,
                CommandParameter = day,
                BorderThickness = new Thickness(0),
                Background = weekend ? new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF1)) : Brushes.Transparent,
                Padding = new Thickness(0, 2, 0, 2),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Cliquer : appliquer le statut choisi à tous les employés affichés, jour " + day
            };

            return button;
        }
    }
}
