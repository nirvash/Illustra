using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.Views;
using NUnit.Framework;
using Prism.Container.DryIoc;
using Prism.Events;
using Prism.Ioc;

namespace Illustra.Tests.Views;

public class PropertyPanelInitialSelectionTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void SamePathInitialSelection_RestoresCollapsedPanelVisibility()
    {
        var app = Application.Current ?? new Application();
        var previousShutdownMode = app.ShutdownMode;
        var previousMainWindow = app.MainWindow;
        var hadFontSize = app.Resources.Contains("AppFontSize");
        var previousFontSize = app.Resources["AppFontSize"];
        const string ratingConverterKey = "RatingToVisibilityConverter";
        var hadRatingConverter = app.Resources.Contains(ratingConverterKey);
        var previousRatingConverter = app.Resources[ratingConverterKey];
        var container = new DryIocContainerExtension();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var events = new EventAggregator();
        var context = (IllustraAppContext)RuntimeHelpers.GetUninitializedObject(typeof(IllustraAppContext));
        var scratchRoot = Environment.GetEnvironmentVariable("TMPDIR");
        string scratch;
        var removeScratchDirectory = false;
        if (!string.IsNullOrWhiteSpace(scratchRoot))
        {
            scratchRoot = Path.Combine(scratchRoot, $"illustra-property-panel-{Guid.NewGuid():N}");
            scratch = scratchRoot;
            removeScratchDirectory = true;
        }
        else
        {
            scratchRoot = Path.Combine(ProjectRoot(), ".scratch");
            scratch = scratchRoot;
            removeScratchDirectory = !Directory.Exists(scratch);
        }
        Directory.CreateDirectory(scratch);
        var path = Path.Combine(scratch, $"illustra-property-panel-{Guid.NewGuid():N}.png");
        var differentPath = Path.Combine(scratch, $"illustra-property-panel-{Guid.NewGuid():N}.png");
        Window? host = null;
        IContainerExtension? previousContainer = null;
        var installedNewContainer = ContainerLocator.TrySetContainerExtension(container);
        try
        {
            if (!installedNewContainer)
            {
                previousContainer = (IContainerExtension)ContainerLocator.Container;
                ContainerLocator.SetContainerExtension(container);
            }
            File.WriteAllBytes(path, new byte[] { 0 });
            File.WriteAllBytes(differentPath, new byte[] { 0 });
            app.Resources["AppFontSize"] = 12d;
            app.Resources[ratingConverterKey] = new RatingToVisibilityConverter();
            typeof(IllustraAppContext).GetField("_currentProperties", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(context, new ImagePropertiesModel { FilePath = path });
            container.RegisterInstance<IEventAggregator>(events);
            container.RegisterInstance(context);
            var panel = new PropertyPanelControl();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            host = new Window { Width = 160, Height = 120, ShowInTaskbar = false, Content = panel };
            host.Show();
            host.UpdateLayout();

            typeof(PropertyPanelControl).GetMethod("OnMcpFolderChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(panel, new object[] { new McpOpenFolderEventArgs() });
            Assert.That(panel.Visibility, Is.EqualTo(Visibility.Collapsed), "folder selection must collapse the panel");

            panel.SetPresentationEnabled(false);
            var lightweightProperties = new ImagePropertiesModel { FilePath = path };
            typeof(IllustraAppContext).GetProperty("CurrentProperties")!
                .SetValue(context, lightweightProperties);
            Assert.That(panel.ImageProperties, Is.Not.SameAs(lightweightProperties),
                "properties must remain deferred while presentation is disabled and the panel is collapsed");

            panel.OnFileSelected(new SelectedFileModel("test", path));

            Assert.Multiple(() =>
            {
                Assert.That(panel.Visibility, Is.EqualTo(Visibility.Visible),
                    "an active same-path selection must restore the panel after folder selection collapsed it");
                Assert.That(panel.IsVisible, Is.True,
                    "the restored panel must be visible in its open host");
                Assert.That(panel.ImageProperties, Is.Not.SameAs(lightweightProperties),
                    "restoring visibility must not flush deferred properties while presentation is disabled");
            });

            panel.SetPresentationEnabled(true);
            Assert.That(panel.ImageProperties, Is.SameAs(lightweightProperties),
                "opening presentation must flush the deferred properties");

            CollapseForFolderChange(panel);
            panel.OnFileSelected(null!);
            panel.OnFileSelected(new SelectedFileModel("test", string.Empty));
            panel.OnFileSelected(new SelectedFileModel("test", Path.Combine(scratch, "missing.png")));
            panel.OnFileSelected(new SelectedFileModel("test", scratch));
            Assert.That(panel.Visibility, Is.EqualTo(Visibility.Collapsed),
                "null, empty, missing-file, and directory selections must keep the folder-hidden panel collapsed");

            panel.OnFileSelected(new SelectedFileModel("test", differentPath));
            Assert.That(panel.Visibility, Is.EqualTo(Visibility.Visible),
                "a valid different-path selection must retain its existing show behavior");
            CollapseForFolderChange(panel);
            panel.OnFileSelected(new SelectedFileModel("test", path));
            Assert.That(panel.Visibility, Is.EqualTo(Visibility.Visible),
                "a valid same-path reselection must restore the panel");
        }
        finally
        {
            host?.Close();
            if (ReferenceEquals(app.MainWindow, host))
                app.MainWindow = previousMainWindow;
            app.ShutdownMode = previousShutdownMode;
            File.Delete(path);
            File.Delete(differentPath);
            if (installedNewContainer)
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
            if (removeScratchDirectory && Directory.Exists(scratch) && Directory.GetFileSystemEntries(scratch).Length == 0)
                Directory.Delete(scratch);
        }
    }

    private static void CollapseForFolderChange(PropertyPanelControl panel)
    {
        typeof(PropertyPanelControl).GetMethod("OnMcpFolderChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(panel, new object[] { new McpOpenFolderEventArgs() });
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Illustra.sln")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null, "Could not locate the solution root.");
        return directory!.FullName;
    }
}
