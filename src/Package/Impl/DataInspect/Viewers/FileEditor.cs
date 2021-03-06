﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Common.Core;
using Microsoft.Common.Core.Shell;
using Microsoft.R.Core.Formatting;
using Microsoft.R.Editor;
using Microsoft.R.Host.Client;
using Microsoft.R.Support.Settings;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.R.Packages.R;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Microsoft.VisualStudio.R.Package.DataInspect.Viewers {
    [Export(typeof(IFileEditor))]
    internal sealed class FileEditor : IFileEditor {
        private readonly ICoreShell _coreShell;
        private readonly IRToolsSettings _settings;
        private readonly IVsEditorAdaptersFactoryService _adapterService;

        [ImportingConstructor]
        public FileEditor(ICoreShell coreShell, IRToolsSettings settings, IVsEditorAdaptersFactoryService adapterService) {
            _coreShell = coreShell;
            _settings = settings;
            _adapterService = adapterService;
        }

        public async Task<string> EditFileAsync(string content, string fileName, CancellationToken cancellationToken = default(CancellationToken)) {
            await TaskUtilities.SwitchToBackgroundThread();

            if (!string.IsNullOrEmpty(content)) {
                var formatter = new RFormatter(_coreShell.GetService<IREditorSettings>().FormatOptions);
                content = formatter.Format(content);

                var fs = _coreShell.FileSystem();
                fileName = Path.ChangeExtension(Path.GetTempFileName(), ".r");
                try {
                    if (fs.FileExists(fileName)) {
                        fs.DeleteFile(fileName);
                    }
                    using (var sw = new StreamWriter(fileName)) {
                        sw.Write(content);
                    }
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            } else {
                fileName = fileName?.FromRPath();
            }

            if (!string.IsNullOrEmpty(fileName)) {
                try {
                    if (!Path.IsPathRooted(fileName)) {
                        fileName = Path.Combine(_settings.WorkingDirectory, fileName);
                    }
                } catch (ArgumentException) {
                    return string.Empty;
                }
                return await new FileEditorWindow(_coreShell, _adapterService, fileName).ShowAsync(cancellationToken);
            }

            return string.Empty;
        }

        private class FileEditorWindow : IVsWindowFrameEvents {
            private readonly ICoreShell _coreShell;
            private readonly IVsEditorAdaptersFactoryService _adapterService;
            private readonly TaskCompletionSource<string> _tcs;
            private readonly string _fileName;
            private volatile IVsWindowFrame _editorFrame;
            private ITextBuffer _textBuffer;
            private IVsUIShell7 _uiShell;
            private uint _cookie;

            public FileEditorWindow(ICoreShell coreShell, IVsEditorAdaptersFactoryService adapterService, string fileName) {
                _coreShell = coreShell;
                _adapterService = adapterService;
                _fileName = fileName;
                _tcs = new TaskCompletionSource<string>();
                _coreShell.Terminating += OnAppTerminating;
            }

            public async Task<string> ShowAsync(CancellationToken cancellationToken) {
                var registration = _tcs.RegisterForCancellation(cancellationToken);
                try {
                    _coreShell.MainThread().Post(Show);
                    return await _tcs.Task;
                } finally {
                    registration.Dispose();
                    if (_tcs.Task.IsCanceled && _editorFrame != null) {
                        _coreShell.MainThread().Post(Close);
                    }
                }
            }

            private void Show() {
                IVsTextView view;
                IVsWindowFrame vsWindowFrame;

                try {
                    IVsUIHierarchy hier;
                    uint itemid;
                    VsShellUtilities.OpenDocument(RPackage.Current, _fileName, VSConstants.LOGVIEWID.Code_guid, out hier, out itemid, out vsWindowFrame, out view);
                } catch (Exception ex) {
                    _coreShell.ShowErrorMessage(Resources.Error_ExceptionAccessingPath.FormatInvariant(_fileName, ex.Message));
                    _tcs.TrySetResult(string.Empty);
                    return;
                }

                _editorFrame = vsWindowFrame;
                if (view == null || _editorFrame == null) {
                    _tcs.TrySetResult(string.Empty);
                    return;
                }

                if (_tcs.Task.IsCompleted) {
                    Close();
                    return;
                }

                IVsTextLines vsTextLines;
                view.GetBuffer(out vsTextLines);
                _textBuffer = _adapterService.GetDataBuffer(vsTextLines);

                _uiShell = _coreShell.GetService<IVsUIShell7>(typeof(SVsUIShell));
                _cookie = _uiShell.AdviseWindowFrameEvents(this);
            }

            private void Close() {
                _editorFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
            }

            private void OnAppTerminating(object sender, EventArgs e) {
                if (_cookie != 0) {
                    UnadviseWindowFrameEvents();
                    _tcs.TrySetCanceled();
                }
            }

            private void UnadviseWindowFrameEvents() {
                _coreShell.AssertIsOnMainThread();
                _uiShell.UnadviseWindowFrameEvents(_cookie);
                _cookie = 0;
            }

            #region IVsWindowFrameEvents
            public void OnFrameDestroyed(IVsWindowFrame frame) {
                if (frame == _editorFrame) {
                    UnadviseWindowFrameEvents();
                    _tcs.TrySetResult(_textBuffer.CurrentSnapshot.GetText());
                }
            }

            public void OnFrameCreated(IVsWindowFrame frame) { }
            public void OnFrameIsVisibleChanged(IVsWindowFrame frame, bool newIsVisible) { }
            public void OnFrameIsOnScreenChanged(IVsWindowFrame frame, bool newIsOnScreen) { }
            public void OnActiveFrameChanged(IVsWindowFrame oldFrame, IVsWindowFrame newFrame) { }
            #endregion
        }
    }
}
