using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.ViewModels;
using Illustra.Views;
using NUnit.Framework;
using Prism.Container.DryIoc;
using Prism.Events;
using Prism.Ioc;

namespace Illustra.Tests.Views;

[NonParallelizable]
public class ThumbnailStartupSelectionTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void ProgrammaticStartupSelection_PublishesOnceAndPanelShowsSelectedPath()
    {
        // Run this regression in its own test process. An Application left behind by another
        // NUnit STA worker may own a dispatcher that is no longer pumping; synchronously
        // invoking that dispatcher would hang the test process.
        var dispatcher = Dispatcher.CurrentDispatcher;
        if (Application.Current is { } existingApp && !ReferenceEquals(existingApp.Dispatcher, dispatcher))
            throw new InvalidOperationException("Run this WPF regression in an isolated test process.");

        RunOnApplicationDispatcher(dispatcher);
    }

    private static void RunOnApplicationDispatcher(Dispatcher ownerDispatcher)
    {
        var app = Application.Current ?? new Application();
        if (!ReferenceEquals(app.Dispatcher, ownerDispatcher))
            throw new InvalidOperationException("The STA harness must run on the WPF Application dispatcher.");
        var previousShutdownMode = app.ShutdownMode;
        var previousMainWindow = app.MainWindow;
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(ownerDispatcher));
        var container = new DryIocContainerExtension();
        var events = new EventAggregator();
        var database = (DatabaseManager)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseManager));
        var previousContainer = ContainerLocator.TrySetContainerExtension(container)
            ? null
            : (IContainerExtension)ContainerLocator.Container;
        var installedContainer = previousContainer == null;
        container.RegisterInstance<IEventAggregator>(events);
        container.RegisterInstance(database);
        var viewModel = new ThumbnailListViewModel();
        var context = (IllustraAppContext)RuntimeHelpers.GetUninitializedObject(typeof(IllustraAppContext));
        typeof(IllustraAppContext).GetField("_currentProperties", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, new ImagePropertiesModel());
        typeof(IllustraAppContext).GetField("_mainViewModel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, viewModel);
        var hadFontSize = app.Resources.Contains("AppFontSize");
        var previousFontSize = app.Resources["AppFontSize"];
        const string ratingConverterKey = "RatingToVisibilityConverter";
        var hadRatingConverter = app.Resources.Contains(ratingConverterKey);
        var previousRatingConverter = app.Resources[ratingConverterKey];
        var fixtureDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "hermes", "profiles", "agent-harness", "cache", "scratch", "nai-startup-fixed-bin", "test-fixtures");
        string path = Path.Combine(fixtureDirectory, $"illustra-startup-selection-{Guid.NewGuid():N}.png");
        string? secondPath = null;
        Window? host = null;
        try
        {
            if (!installedContainer)
                ContainerLocator.SetContainerExtension(container);
            container.RegisterInstance(context);

            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.Resources["AppFontSize"] = 12d;
            app.Resources[ratingConverterKey] = new RatingToVisibilityConverter();
            Directory.CreateDirectory(fixtureDirectory);
            File.WriteAllBytes(path, new byte[] { 1 });
            var node = new FileNodeModel(path);
            viewModel.AddItem(node);

            var panel = new PropertyPanelControl();
            panel.SetPresentationEnabled(false);
            host = new Window { Width = 220, Height = 160, ShowInTaskbar = false, Content = panel };
            host.Show();
            host.UpdateLayout();
            panel.Visibility = Visibility.Collapsed;
            Assert.That(panel.Visibility, Is.EqualTo(Visibility.Collapsed));

            var notifications = 0;
            events.GetEvent<FileSelectedEvent>().Subscribe(selected =>
            {
                notifications++;
                typeof(IllustraAppContext).GetProperty("CurrentProperties")!
                    .SetValue(context, new ImagePropertiesModel { FilePath = selected.FullPath });
            });

            var list = new ListView { ItemsSource = viewModel.FilteredItems, SelectionMode = SelectionMode.Extended };
            var control = (ThumbnailListControl)RuntimeHelpers.GetUninitializedObject(typeof(ThumbnailListControl));
            typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(control, Dispatcher.CurrentDispatcher);
            SetField(control, "ThumbnailItemsControl", list);
            SetField(control, "_viewModel", viewModel);
            SetField(control, "_appContext", context);
            SetField(control, "_eventAggregator", events);
            var viewModelPropertyChanged = (System.ComponentModel.PropertyChangedEventHandler)Delegate.CreateDelegate(
                typeof(System.ComponentModel.PropertyChangedEventHandler), control,
                typeof(ThumbnailListControl).GetMethod("OnViewModelPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!);
            viewModel.PropertyChanged += viewModelPropertyChanged;
            var guardedSelectionChanges = 0;
            list.SelectionChanged += (_, _) =>
            {
                if ((bool)GetField(control, "_isUpdatingSelection")!)
                    guardedSelectionChanges++;
            };
            list.SelectionChanged += (SelectionChangedEventHandler)Delegate.CreateDelegate(
                typeof(SelectionChangedEventHandler), control,
                typeof(ThumbnailListControl).GetMethod("ThumbnailItemsControl_SelectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!);

            // Mirrors LoadFileNodesAsync startup selection: the ViewModel selects the initial node,
            // then the real ViewModel-to-UI synchronization raises guarded SelectionChanged.
            viewModel.SelectedItems.Add(node);
            PumpDispatcherUntil(() => notifications > 0, TimeSpan.FromSeconds(3));

            Assert.Multiple(() =>
            {
                Assert.That(notifications, Is.EqualTo(1), "the finalized programmatic selection must publish once");
                Assert.That(guardedSelectionChanges, Is.GreaterThan(0), "the startup UI sync must exercise the existing SelectionChanged guard");
                Assert.That(list.SelectedItems.Contains(node), Is.True, "startup selection must reach the UI");
                Assert.That(context.CurrentProperties.FilePath, Is.EqualTo(path));
                Assert.That(panel.Visibility, Is.EqualTo(Visibility.Visible), "the real panel subscriber must show a valid selection");
            });

            panel.SetPresentationEnabled(true);
            Assert.That(panel.ImageProperties?.FilePath, Is.EqualTo(path), "opening the panel must present the selected path");
            typeof(ThumbnailListControl).GetMethod("UpdateUISelection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(control, null);
            Assert.That(notifications, Is.EqualTo(1), "re-syncing the same ViewModel selection must not publish again");

            secondPath = Path.Combine(fixtureDirectory, $"illustra-startup-selection-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(secondPath, new byte[] { 1 });
            var secondNode = new FileNodeModel(secondPath);
            viewModel.AddItem(secondNode);
            list.SelectedItems.Add(secondNode);
            Assert.Multiple(() =>
            {
                Assert.That(notifications, Is.EqualTo(2), "a multi-selection user change must publish once, without a duplicate from VM synchronization");
                Assert.That(context.CurrentProperties.FilePath, Is.EqualTo(secondPath), "the last user-selected item remains the published item");
            });

            list.SelectedItems.Clear();
            Assert.That(notifications, Is.EqualTo(2), "clearing selection keeps the existing no-file-notification behavior");
        }
        finally
        {
            host?.Close();
            if (ReferenceEquals(app.MainWindow, host))
                app.MainWindow = previousMainWindow;
            app.ShutdownMode = previousShutdownMode;
            File.Delete(path);
            if (secondPath != null)
                File.Delete(secondPath);
            if (installedContainer)
                ContainerLocator.ResetContainer();
            else
                ContainerLocator.SetContainerExtension(previousContainer!);
            if (hadFontSize)
                app.Resources["AppFontSize"] = previousFontSize;
            else
                app.Resources.Remove("AppFontSize");
            if (hadRatingConverter)
                app.Resources[ratingConverterKey] = previousRatingConverter;
            else
                app.Resources.Remove(ratingConverterKey);
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    private static void SetField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static object? GetField(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

    private static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        var stopAt = DateTime.UtcNow + timeout;
        timer.Tick += (_, _) =>
        {
            if (condition() || DateTime.UtcNow >= stopAt)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
