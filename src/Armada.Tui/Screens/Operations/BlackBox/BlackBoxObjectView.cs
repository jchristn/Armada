namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Armada.Client.Models;
    using Armada.Client;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit.Input;
    using TUIKit;

    /// <summary>
    /// Paged viewer for one black box evidence object: reads <see cref="ChunkSize"/> bytes at a time with range reads
    /// (<c>GET /api/v1/blackbox/{id}/objects/{name}?offset=&amp;length=</c>), so a large log never loads whole. <c>]</c>
    /// reads the next chunk, <c>[</c> the previous one; the modal footer shows the byte range. Not thread-safe.
    /// </summary>
    public class BlackBoxObjectView : JsonOrTextViewer
    {
        #region Public-Members

        /// <summary>
        /// Bytes per read (the MCP tool's 64 KB page).
        /// </summary>
        public const int ChunkSize = 65536;

        /// <summary>
        /// Entry id.
        /// </summary>
        public string EntryId { get; }

        /// <summary>
        /// Object name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Offset of the chunk shown.
        /// </summary>
        public long Offset { get; private set; } = 0;

        /// <summary>
        /// Object size in bytes (0 until the first read).
        /// </summary>
        public long TotalBytes { get; private set; } = 0;

        /// <summary>
        /// Offset of the next chunk, or null at the end.
        /// </summary>
        public long? NextOffset { get; private set; } = null;

        /// <summary>
        /// Bytes in the chunk shown.
        /// </summary>
        public int ChunkBytes { get; private set; } = 0;

        /// <summary>
        /// True while a read is in flight.
        /// </summary>
        public bool Loading { get; private set; } = false;

        /// <summary>
        /// The modal hosting the viewer (its footer shows the range), or null.
        /// </summary>
        public ViewerModal? Modal { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly OpsScreen _Screen;
        private readonly Stack<long> _Previous = new Stack<long>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="name">Object name.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxObjectView(OpsScreen screen, string entryId, string name) : base("")
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
            EntryId = entryId ?? throw new ArgumentNullException(nameof(entryId));
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open a paged viewer for an object and read its first chunk.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="name">Object name.</param>
        /// <returns>The viewer.</returns>
        public static BlackBoxObjectView Open(OpsScreen screen, string entryId, string name)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            BlackBoxObjectView view = new BlackBoxObjectView(screen, entryId, name);
            ViewerModal modal = new ViewerModal(name, view, screen.Context.Loc, screen.Context.Theme.Current);
            modal.WidthRatio = 0.95;
            modal.HeightRatio = 0.92;
            modal.CopyRequested += (s, e) => screen.Context.Clipboard.Copy(view.PlainText, "Object chunk");
            view.Modal = modal;
            view.UpdateFooter();
            screen.Context.Modals.Show(modal);
            view.Read(0);
            return view;
        }

        /// <summary>
        /// Read the chunk at an offset.
        /// </summary>
        /// <param name="offset">Byte offset.</param>
        public void Read(long offset)
        {
            Loading = true;
            UpdateFooter();
            long at = Math.Max(0, offset);
            _Screen.Call((c, t) => c.ReadBlackBoxObjectAsync(EntryId, Name, at, ChunkSize, t), content =>
            {
                Loading = false;
                Offset = content.Offset;
                TotalBytes = content.TotalBytes;
                NextOffset = content.NextOffset;
                ChunkBytes = content.Content.Length;
                Text = Decode(content);
                UpdateFooter();
            }, null, ex =>
            {
                Loading = false;
                Text = ex is ArmadaApiException api && api.StatusCode == 410
                    ? _Screen.Tr("The object is missing from storage (BlackBoxObjectMissing).")
                    : _Screen.Tr("The object could not be read: {{message}}", LocalizationArgs.Of("message", ex.Message));
                UpdateFooter();
            });
        }

        /// <summary>
        /// Read the next chunk when there is one.
        /// </summary>
        /// <returns>True when a read started.</returns>
        public bool Next()
        {
            if (Loading || NextOffset == null) return false;
            _Previous.Push(Offset);
            Read(NextOffset.Value);
            return true;
        }

        /// <summary>
        /// Read the previous chunk when there is one.
        /// </summary>
        /// <returns>True when a read started.</returns>
        public bool Previous()
        {
            if (Loading || _Previous.Count == 0) return false;
            Read(_Previous.Pop());
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (!Searching && key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None)
            {
                if (key.Rune == ']') return Next() || true;
                if (key.Rune == '[') return Previous() || true;
            }

            return base.HandleKey(key);
        }

        #endregion

        #region Private-Methods

        private void UpdateFooter()
        {
            if (Modal == null) return;
            string range = Loading ? _Screen.Tr("Loading...")
                : TotalBytes == 0 ? "0 B"
                : (Offset + 1).ToString("N0", CultureInfo.InvariantCulture) + "-" + (Offset + ChunkBytes).ToString("N0", CultureInfo.InvariantCulture) + " / " + TotalBytes.ToString("N0", CultureInfo.InvariantCulture) + " B";
            Modal.FooterHint = " " + range + "   [ ] " + _Screen.Tr("Chunks") + "  / " + _Screen.Tr("Search") + "  y " + _Screen.Tr("Copy") + "  Esc " + _Screen.Tr("Close") + " ";
        }

        private string Decode(BlackBoxObjectContent content)
        {
            byte[] bytes = content.Content ?? Array.Empty<byte>();
            if (bytes.Length == 0) return _Screen.Tr("(empty)");
            bool text = content.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || content.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                || content.ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase) || !bytes.Take(4096).Any(b => b == 0);
            if (!text) return _Screen.Tr("(binary object, {{size}}; download the entry to read it)", LocalizationArgs.Of("size", BlackBoxText.Bytes(content.TotalBytes)));
            return Encoding.UTF8.GetString(bytes);
        }

        #endregion
    }
}
