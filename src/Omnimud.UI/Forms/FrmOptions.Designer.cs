namespace Omnimud.UI.Forms
{
    partial class FrmOptions
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                _previewFont?.Dispose();
                foreach (var font in _retiredFonts) font.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
