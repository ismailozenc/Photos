using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;

namespace Photos.Desktop
{
    /// <summary>
    /// Shows message dialogs as standard WinForms windows on Windows. The drawn dialog is kept
    /// on other platforms.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class WinFormsDialogs
    {
        public static void Register()
        {
            Photos.Lib.NativeDialogs.Show = Show;
            Photos.Lib.NativeDialogs.PasswordPrompt = ShowPasswordPrompt;
        }

        /// <summary>
        /// Runs a WinForms dialog on its own single-threaded apartment thread, so the render thread keeps drawing.
        /// The result goes to onDone on that thread; callers hand it back to the render thread.
        /// </summary>
        static void RunOnStaThread<T>(Func<T> show, Action<T> onDone)
        {
            var thread = new Thread(() => onDone(show())) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        static void ShowPasswordPrompt(string title, Action<string> done)
        {
            RunOnStaThread(() => ShowPasswordForm(title), result => Photos.Lib.NativeDialogs.Post(() => done(result)));
        }

        static string ShowPasswordForm(string title)
        {
            using var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(320, 110),
            };
            var box = new TextBox { UseSystemPasswordChar = true, Location = new Point(12, 14), Width = 296 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(146, 48), Width = 75 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(233, 48), Width = 75 };
            form.Controls.AddRange(new Control[] { box, ok, cancel });
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            return form.ShowDialog() == DialogResult.OK ? box.Text : null;
        }

        static bool Show(string[] text, Photos.Lib.MessageBoxButton[] options)
        {
            // The chosen button's action runs on the render thread once the form has closed.
            RunOnStaThread(() => ShowForm(text, options), chosen =>
            {
                if (chosen != null)
                    Photos.Lib.NativeDialogs.Post(chosen);
            });
            return true;
        }

        static Action ShowForm(string[] text, Photos.Lib.MessageBoxButton[] options)
        {
            Action chosen = null;

            using var form = new Form
            {
                Text = Photos.Core.Utils.AppName,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterScreen,
                AutoScaleMode = AutoScaleMode.Font,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12),
            };

            var label = new Label
            {
                Text = string.Join(Environment.NewLine, text),
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 12),
            };

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Bottom,
                WrapContents = false,
            };

            foreach (var option in options)
            {
                var action = option.Action;
                var button = new Button
                {
                    Text = option.Text,
                    AutoSize = true,
                    MinimumSize = new Size(80, 0),
                };
                button.Click += (_, _) =>
                {
                    chosen = action;
                    form.Close();
                };
                buttons.Controls.Add(button);
            }

            if (buttons.Controls.Count > 0)
                form.AcceptButton = (IButtonControl)buttons.Controls[0];

            form.Controls.Add(label);
            form.Controls.Add(buttons);
            form.ShowDialog();
            return chosen;
        }
    }
}
