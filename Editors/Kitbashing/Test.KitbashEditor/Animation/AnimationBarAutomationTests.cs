using System.Windows;
using System.Windows.Automation.Peers;
using KitbasherEditor.Views.EditorViews;
using Shared.Core.Services;
using Shared.Ui.Common.ValueConverters;

namespace Test.KitbashEditor.Animation
{
    // The Animation bar's transport buttons were named by their glyphs ("⏮︎", "⏪") and had no automation id, so a screen
    // reader read out symbols and an automation client had to type them to find a button.
    public class AnimationBarAutomationTests
    {
        static readonly string[] s_transport = ["FirstFrame", "PreviousFrame", "PlayPause", "NextFrame", "LastFrame"];

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EveryTransportButton_HasAnIdAndALocalisedName()
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources["BoolToCollapsedConverter"] = new BoolToVisibilityConverter { TrueValue = Visibility.Visible, FalseValue = Visibility.Collapsed };
            var window = new Window { Content = new AnimationPlayerView(), ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 600, Height = 300 };
            window.Show();
            try
            {
                // Without the app's localisation, a {loc:Loc} name is its key.
                // The bar's own on/off toggle, the five transport buttons, then the animation picker's expander header.
                var buttons = Descendants(UIElementAutomationPeer.CreatePeerForElement(window))
                    .Where(x => x.GetAutomationControlType() == AutomationControlType.Button)
                    .ToList();
                Assert.That(buttons, Has.Count.EqualTo(7));
                Assert.That(buttons[0].GetName(), Is.EqualTo("KitbashTool.AnimationView.Toggle"));
                buttons = buttons.GetRange(1, 5);
                Assert.That(buttons.Select(x => x.GetAutomationId()), Is.EqualTo(s_transport));
                Assert.That(buttons.Select(x => x.GetName()), Is.EqualTo(s_transport.Select(x => "KitbashTool.AnimationView." + x)));
            }
            finally
            {
                window.Close();
            }

            var english = new LocalizationManager();
            english.LoadLanguage("en");
            Assert.That(s_transport.Select(x => english.Get("KitbashTool.AnimationView." + x)), Is.EqualTo(new[] { "First frame", "Previous frame", "Play or pause", "Next frame", "Last frame" }));
        }

        static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer) => (peer.GetChildren() ?? []).SelectMany(x => Descendants(x).Prepend(x));
    }
}
