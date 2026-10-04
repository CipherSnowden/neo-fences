using System.Runtime.InteropServices;
using NeoFences.Core.Items;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using ComFormat = System.Runtime.InteropServices.ComTypes.FORMATETC;
using ComMedium = System.Runtime.InteropServices.ComTypes.STGMEDIUM;

namespace NeoFences.Shell;

/// <summary>What a fence does with drops; NeoFences.App supplies these (all called on the UI thread).</summary>
/// <param name="HitTest">The insert position under a screen point (physical pixels); hovering a tab header shows that tab.</param>
/// <param name="AcceptsDrops">False for the Game Library (it shows NeoFences' own shortcuts only).</param>
/// <param name="ItemsDropped">Keys dragged out of a fence (this process), the insert position, and Ctrl held (duplicate).</param>
/// <param name="TargetsDropped">Paths, or one website URL, dragged in from outside (Explorer, the desktop, a browser).</param>
/// <param name="ShowFeedback">The insert caret's position, or null to hide it.</param>
/// <param name="LogFailure">A drop that could not be read.</param>
public sealed record FenceDropHandlers(
    Func<int, int, int> HitTest,
    Func<bool> AcceptsDrops,
    Action<IReadOnlyList<string>, int, bool> ItemsDropped,
    Action<IReadOnlyList<string>, int> TargetsDropped,
    Action<int?> ShowFeedback,
    Action<Exception> LogFailure);

/// <summary>
/// Drag-drop with Windows' own engine (M3b; M18, ADR-040: virtual items). A drop on a fence only ever creates or moves
/// NeoFences' items: an outside drag is answered as a link, never a move, so Windows never moves or deletes the source
/// (bug K5 cannot happen). Dragging out offers copy or link only: Explorer copies, the original never leaves.
/// </summary>
public static class ShellDragDrop
{
    /// <summary>The keys being dragged out of a fence right now (one drag at a time).</summary>
    internal static IReadOnlyList<string>? CurrentDrag { get; private set; }

    private static readonly ushort UrlFormat = (ushort)PInvoke.RegisterClipboardFormat("UniformResourceLocatorW");
    private static readonly ushort FileGroupDescriptorFormat = (ushort)PInvoke.RegisterClipboardFormat("FileGroupDescriptorW");
    private const ushort UnicodeTextFormat = 13; // CF_UNICODETEXT

    /// <summary>
    /// Starts a drag of fence items; returns when it ends. Other apps get the <paramref name="files"/> (Windows' own data:
    /// Explorer copies them) or, with no files, the <paramref name="urls"/> as links; fences get the <paramref name="keys"/>.
    /// </summary>
    /// <returns>False when the drag could not start.</returns>
    public static bool TryDrag(nint ownerHandle, IReadOnlyList<string> keys, IReadOnlyList<string> files, IReadOnlyList<string> urls,
        Action<Exception> logFailure)
    {
        ComDataObject? dataObject = null;
        try
        {
            dataObject = CreateDataObject(files);
            if (files.Count == 0 && urls.Count > 0)
            {
                SetText(dataObject, UrlFormat, urls[0]);
                SetText(dataObject, UnicodeTextFormat, string.Join(Environment.NewLine, urls));
            }
            CurrentDrag = keys;
            // Copy or link only, never move (hard rule 1): whatever the target decides, the original stays where it is.
            PInvoke.SHDoDragDrop((HWND)ownerHandle, dataObject, null, DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_LINK, out _);
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure);
            return false;
        }
        finally
        {
            CurrentDrag = null;
            if (dataObject is not null) Marshal.ReleaseComObject(dataObject);
        }
    }

    /// <summary>Makes the fence window a drop target. Dispose (before the window is destroyed) to unregister.</summary>
    public static IDisposable RegisterFence(nint fenceHandle, FenceDropHandlers handlers)
    {
        var target = new FenceDropTarget((HWND)fenceHandle, handlers);
        // WPF registers its own drop target on every window; NeoFences does not use WPF drag-drop, so replace it.
        PInvoke.RevokeDragDrop((HWND)fenceHandle);
        PInvoke.RegisterDragDrop((HWND)fenceHandle, target).ThrowOnFailure();
        return target;
    }

    /// <summary>Windows' data object for these files (any folders, absolute ID lists), or an empty one. Missing files are left out.</summary>
    private static unsafe ComDataObject CreateDataObject(IReadOnlyList<string> files)
    {
        var idLists = new List<nint>();
        try
        {
            foreach (var file in files)
            {
                if (PInvoke.SHParseDisplayName(file, null, out var idList, 0, out _).Succeeded) idLists.Add((nint)idList);
            }
            var interfaceId = typeof(ComDataObject).GUID;
            fixed (nint* ids = idLists.ToArray())
            {
                PInvoke.SHCreateDataObject(null, (uint)idLists.Count, (ITEMIDLIST**)ids, null, &interfaceId, out var created).ThrowOnFailure();
                return (ComDataObject)created;
            }
        }
        finally
        {
            foreach (var idList in idLists) Marshal.FreeCoTaskMem(idList);
        }
    }

    private static void SetText(ComDataObject dataObject, ushort format, string text)
    {
        var formatEtc = new ComFormat { cfFormat = unchecked((short)format), dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL };
        var medium = new ComMedium { tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL, unionmember = Marshal.StringToHGlobalUni(text) };
        dataObject.SetData(ref formatEtc, ref medium, true); // the data object owns the memory now
    }

    /// <summary>True when the source describes virtual files (zip contents, phones, mail attachments, a browser's link).</summary>
    private static unsafe bool OffersVirtualFiles(IDataObject dataObject)
    {
        var format = Format(FileGroupDescriptorFormat);
        try { return dataObject.QueryGetData(&format).Value == 0; } // S_OK; S_FALSE and errors mean no
        catch (Exception failure) when (failure is not OutOfMemoryException) { return false; }
    }

    /// <summary>
    /// What an outside drag brings: its files (CF_HDROP), else one website (a browser's link). Virtual files (zip contents,
    /// a phone) are never asked for CF_HDROP — that would extract every file — and have no target an item could point at.
    /// </summary>
    internal static IReadOnlyList<string> DroppedTargets(IDataObject dataObject)
    {
        if (!OffersVirtualFiles(dataObject) && DroppedFiles(dataObject) is { Count: > 0 } files) return files;
        var url = ReadText(dataObject, UrlFormat) ?? ReadText(dataObject, UnicodeTextFormat);
        return ItemKinds.Clean(url?.Split('\n')[0]) is { } website && ItemKinds.IsWebsite(website) ? [website] : [];
    }

    private static FORMATETC Format(ushort format) => new()
    {
        cfFormat = format,
        dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        tymed = (uint)TYMED.TYMED_HGLOBAL,
    };

    /// <summary>A text format's content (bounded by its memory block: the source is another program), or null.</summary>
    private static unsafe string? ReadText(IDataObject dataObject, ushort format)
    {
        var formatEtc = Format(format);
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&formatEtc, out medium);
            if (medium.tymed != TYMED.TYMED_HGLOBAL) return null;
            var size = (long)(nuint)PInvoke.GlobalSize(medium.u.hGlobal);
            var text = (char*)PInvoke.GlobalLock(medium.u.hGlobal);
            if (text is null) return null;
            try
            {
                var span = new ReadOnlySpan<char>(text, (int)Math.Min(size / sizeof(char), 8192));
                var end = span.IndexOf('\0');
                return new string(end >= 0 ? span[..end] : span).Trim();
            }
            finally
            {
                PInvoke.GlobalUnlock(medium.u.hGlobal);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
    }

    /// <summary>Paths in the drop's file list (CF_HDROP); empty when there is none.</summary>
    private static unsafe List<string> DroppedFiles(IDataObject dataObject)
    {
        var files = new List<string>();
        var format = new FORMATETC
        {
            cfFormat = (ushort)CLIPBOARD_FORMAT.CF_HDROP,
            dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)TYMED.TYMED_HGLOBAL,
        };
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&format, out medium);
            var drop = (HDROP)(nint)medium.u.hGlobal.Value;
            var count = PInvoke.DragQueryFile(drop, uint.MaxValue, default, 0);
            var buffer = new char[32768];
            for (uint index = 0; index < count; index++)
            {
                var length = PInvoke.DragQueryFile(drop, index, buffer);
                files.Add(new string(buffer, 0, (int)length));
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // No file list: nothing to place.
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
        return files;
    }

    /// <summary>
    /// IDropTarget for one fence: items from fences move (Ctrl: duplicate); files and links from outside become items.
    /// Keeps Windows' drag image visible over the fence (IDropTargetHelper).
    /// </summary>
    private sealed unsafe class FenceDropTarget(HWND fence, FenceDropHandlers handlers) : IDropTarget, IDisposable
    {
        private IReadOnlyList<string> _keys = [];    // dragged out of a fence (this process)
        private IReadOnlyList<string> _targets = []; // brought from outside (read once, at DragEnter)
        private IDropTargetHelper? _imageHelper;

        public void DragEnter(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            Reset();
            _keys = CurrentDrag ?? [];
            if (_keys.Count == 0) _targets = DroppedTargets(pDataObj);
            _imageHelper = TryCreateImageHelper();
            Update(grfKeyState, pt, pdwEffect);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragEnter(fence, pDataObj, &point, effect);
            });
        }

        public void DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            Update(grfKeyState, pt, pdwEffect);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragOver(&point, effect);
            });
        }

        public void DragLeave()
        {
            WithImageHelper(helper => helper.DragLeave());
            handlers.ShowFeedback(null);
            Reset();
        }

        public void Drop(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            try
            {
                var insertAt = Update(grfKeyState, pt, pdwEffect);
                var effect = *pdwEffect;
                WithImageHelper(helper =>
                {
                    var point = new System.Drawing.Point(pt.x, pt.y);
                    helper.Drop(pDataObj, &point, effect);
                });
                if (effect == DROPEFFECT.DROPEFFECT_NONE || insertAt is not { } position) return;
                if (_keys.Count > 0)
                {
                    handlers.ItemsDropped(_keys, position, grfKeyState.HasFlag(MODIFIERKEYS_FLAGS.MK_CONTROL));
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE; // our own drag: nothing for the source to do
                }
                else
                {
                    handlers.TargetsDropped(_targets, position); // the effect stays a link (or a copy): never a move
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            }
            finally
            {
                handlers.ShowFeedback(null);
                Reset();
            }
        }

        public void Dispose()
        {
            PInvoke.RevokeDragDrop(fence);
            Reset();
        }

        /// <summary>Where the drag would land now (null: refused) and the effect shown: never a move.</summary>
        private int? Update(MODIFIERKEYS_FLAGS keys, POINTL pt, DROPEFFECT* effect)
        {
            var allowed = *effect;
            *effect = DROPEFFECT.DROPEFFECT_NONE;
            try
            {
                var insertAt = handlers.HitTest(pt.x, pt.y); // first: a hovered tab header switches the tab, which may refuse
                if (!handlers.AcceptsDrops() || (_keys.Count == 0 && _targets.Count == 0))
                {
                    handlers.ShowFeedback(null);
                    return null;
                }
                handlers.ShowFeedback(insertAt);
                // Fence items: Ctrl duplicates ("+"), a plain drag moves them (shown as a link: the drag offers no move,
                // so Explorer can never take the original). From outside: a link, or a copy when the source offers no link.
                var duplicate = _keys.Count > 0 && keys.HasFlag(MODIFIERKEYS_FLAGS.MK_CONTROL);
                *effect = duplicate && allowed.HasFlag(DROPEFFECT.DROPEFFECT_COPY) ? DROPEFFECT.DROPEFFECT_COPY
                    : allowed.HasFlag(DROPEFFECT.DROPEFFECT_LINK) ? DROPEFFECT.DROPEFFECT_LINK
                    : allowed.HasFlag(DROPEFFECT.DROPEFFECT_COPY) ? DROPEFFECT.DROPEFFECT_COPY
                    : DROPEFFECT.DROPEFFECT_NONE;
                return *effect == DROPEFFECT.DROPEFFECT_NONE ? null : insertAt;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                handlers.ShowFeedback(null);
                return null;
            }
        }

        /// <summary>The drag image is cosmetic: a failing helper must not break the drop or leave state behind.</summary>
        private void WithImageHelper(Action<IDropTargetHelper> call)
        {
            if (_imageHelper is null) return;
            try { call(_imageHelper); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
        }

        private void Reset()
        {
            _keys = [];
            _targets = [];
            if (_imageHelper is not null) Marshal.ReleaseComObject(_imageHelper);
            _imageHelper = null;
        }

        private static IDropTargetHelper? TryCreateImageHelper()
        {
            try
            {
                return (IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(PInvoke.CLSID_DragDropHelper)!)!;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                return null; // no drag image over fences; the drop still works
            }
        }
    }
}
