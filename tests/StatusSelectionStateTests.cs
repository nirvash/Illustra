using System.Collections.Generic;
using Illustra.Events;
using Illustra.Models;
using Illustra.ViewModels;
using NUnit.Framework;
using Prism.Events;

namespace Illustra.Tests
{
    public class StatusSelectionStateTests
    {
        [Test]
        public void SingleSelection_ShowsOnlyBasenameAndTracksSelectionChanges()
        {
            var state = new StatusSelectionState();

            state.OnFileSelected(@"C:\Pictures\first.png");
            state.OnSelectionCountChanged(1);
            Assert.That(state.SelectedFileName, Is.EqualTo("first.png"));

            state.OnFileSelected(@"D:\Other\second.jpg");
            Assert.That(state.SelectedFileName, Is.EqualTo("second.jpg"));

            state.OnSelectionCountChanged(2);
            Assert.That(state.SelectedFileName, Is.Null);
            state.OnFileSelected(@"C:\Pictures\stale.png");
            state.OnSelectionCountChanged(1);
            Assert.That(state.SelectedFileName, Is.EqualTo("stale.png"));
            state.OnFileSelected(@"D:\Other\second.jpg");
            Assert.That(state.SelectedFileName, Is.EqualTo("second.jpg"));

            state.OnFileSelected(string.Empty);
            Assert.That(state.SelectedFileName, Is.Null);
            state.OnFileSelected(@"C:\Pictures\third.webp");
            Assert.That(state.SelectedFileName, Is.EqualTo("third.webp"));

            state.OnSelectionCountChanged(0);
            Assert.That(state.SelectedFileName, Is.Null);
        }

        [Test]
        public void MultipleToSingle_WithFileSelectedBeforeCount_ShowsNewBasename()
        {
            var state = new StatusSelectionState();
            state.OnSelectionCountChanged(2);

            // ThumbnailListControl.SelectionChanged publishes these in this order.
            state.OnFileSelected(@"C:\Pictures\new.png");
            state.OnSelectionCountChanged(1);

            Assert.That(state.SelectedFileName, Is.EqualTo("new.png"));
        }

        [Test]
        public void SelectionState_HandlesBothEventOrdersAndClearsWithoutStaleNames()
        {
            var state = new StatusSelectionState();

            state.OnSelectionCountChanged(1);
            state.OnFileSelected(@"C:\Pictures\count-first.png");
            Assert.That(state.SelectedFileName, Is.EqualTo("count-first.png"));

            state.OnFileSelected(@"C:\Pictures\file-first.png");
            state.OnSelectionCountChanged(1);
            Assert.That(state.SelectedFileName, Is.EqualTo("file-first.png"));

            state.OnSelectionCountChanged(2);
            Assert.That(state.SelectedFileName, Is.Null);
            state.OnSelectionCountChanged(0);
            Assert.That(state.SelectedFileName, Is.Null);

            state.OnSelectionCountChanged(1);
            state.OnFileSelected(@"C:\Pictures\before-clear.png");
            state.Clear();
            Assert.That(state.SelectedFileName, Is.Null);
            state.OnSelectionCountChanged(1);
            Assert.That(state.SelectedFileName, Is.Null);
        }

        [Test]
        public void FinalizedProgrammaticSelection_PublishesFileAndMainWindowCount()
        {
            var events = new EventAggregator();
            SelectedFileModel? selectedFile = null;
            SelectionCountChangedEventArgs? selectedCount = null;
            var publishOrder = new List<string>();
            events.GetEvent<FileSelectedEvent>().Subscribe(args =>
            {
                selectedFile = args;
                publishOrder.Add("file");
            });
            events.GetEvent<SelectionCountChangedEvent>().Subscribe(args =>
            {
                selectedCount = args;
                publishOrder.Add("count");
            });

            SelectionStatusEventPublisher.PublishFinalizedSelection(events, @"C:\Pictures\initial.png", 1, isMainWindowControl: true);

            Assert.That(selectedFile?.FullPath, Is.EqualTo(@"C:\Pictures\initial.png"));
            Assert.That(selectedCount?.SelectedCount, Is.EqualTo(1));
            Assert.That(selectedCount?.SourceId, Is.EqualTo(SelectionStatusEventPublisher.MainWindowSourceId));
            Assert.That(selectedCount?.SelectedFilePath, Is.EqualTo(@"C:\Pictures\initial.png"));
            Assert.That(publishOrder, Is.EqualTo(new[] { "file", "count" }));
        }

        [Test]
        public void NonMainWindowSelection_DoesNotPublishMainWindowCountSource()
        {
            var events = new EventAggregator();
            SelectionCountChangedEventArgs? selectedCount = null;
            events.GetEvent<SelectionCountChangedEvent>().Subscribe(args => selectedCount = args);

            SelectionStatusEventPublisher.PublishFinalizedSelection(events, @"C:\Pictures\viewer.png", 1, isMainWindowControl: false);

            Assert.That(selectedCount?.SourceId, Is.Not.EqualTo(SelectionStatusEventPublisher.MainWindowSourceId));
            Assert.That(selectedCount?.SelectedFilePath, Is.Null);
        }

        [Test]
        public void SameFolderViewerFileSelection_DoesNotChangeMainWindowFilename()
        {
            var events = new EventAggregator();
            var mainState = new StatusSelectionState();
            events.GetEvent<SelectionCountChangedEvent>().Subscribe(
                args => mainState.OnSelectionCountChanged(args.SelectedCount, args.SelectedFilePath),
                ThreadOption.PublisherThread,
                false,
                args => args.SourceId == SelectionStatusEventPublisher.MainWindowSourceId);

            SelectionStatusEventPublisher.PublishFinalizedSelection(
                events, @"C:\Pictures\main.png", 1, isMainWindowControl: true);
            Assert.That(mainState.SelectedFileName, Is.EqualTo("main.png"));

            SelectionStatusEventPublisher.PublishFinalizedSelection(
                events, @"C:\Pictures\viewer.png", 1, isMainWindowControl: false);

            Assert.That(mainState.SelectedFileName, Is.EqualTo("main.png"));
        }

        [Test]
        public void MainWindowCountOnlySubscription_AtomicallyTracksSelectionAndClear()
        {
            var events = new EventAggregator();
            var state = new StatusSelectionState();
            events.GetEvent<SelectionCountChangedEvent>().Subscribe(
                args => state.OnSelectionCountChanged(args.SelectedCount, args.SelectedFilePath),
                ThreadOption.PublisherThread,
                false,
                args => args.SourceId == SelectionStatusEventPublisher.MainWindowSourceId);

            SelectionStatusEventPublisher.PublishFinalizedSelection(
                events, @"C:\Pictures\single.png", 1, isMainWindowControl: true);
            Assert.That(state.SelectedFileName, Is.EqualTo("single.png"));

            SelectionStatusEventPublisher.PublishFinalizedSelection(
                events, null, 2, isMainWindowControl: true);
            Assert.That(state.SelectedFileName, Is.Null);

            SelectionStatusEventPublisher.PublishFinalizedSelection(
                events, @"C:\Pictures\new-single.png", 1, isMainWindowControl: true);
            Assert.That(state.SelectedFileName, Is.EqualTo("new-single.png"));

            SelectionStatusEventPublisher.PublishFinalizedSelection(
                events, null, 0, isMainWindowControl: true);
            Assert.That(state.SelectedFileName, Is.Null);

            state.Clear();
            SelectionStatusEventPublisher.PublishFinalizedSelection(
                events, null, 1, isMainWindowControl: true);
            Assert.That(state.SelectedFileName, Is.Null);

            var legacyArgs = new SelectionCountChangedEventArgs(1);
            Assert.That(legacyArgs.SourceId, Is.EqualTo("ThumbnailList"));
            Assert.That(legacyArgs.SourceId, Is.Not.EqualTo(SelectionStatusEventPublisher.MainWindowSourceId));
            Assert.That(legacyArgs.SelectedFilePath, Is.Null);
        }
    }
}
