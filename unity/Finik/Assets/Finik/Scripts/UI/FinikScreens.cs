using System.Collections.Generic;
using UnityEngine;

namespace Finik.UI
{
    /// <summary>A full-screen menu over the room: the shop, the piggy bank, the quest board, …</summary>
    public interface IFinikScreen
    {
        /// <summary>True from the moment the menu takes the room until it has fully let go of it.</summary>
        bool IsOpen { get; }

        /// <summary>
        /// True for a menu that takes the room over while it is up: it switches the HUD off, holds the
        /// camera on Finik and stops him moving. A card that simply lies over the running room — the
        /// child's own progress — says false, and the menu it replaces hands the room back as usual.
        /// </summary>
        bool HoldsRoom { get; }

        void Close();
    }

    /// <summary>
    /// Keeps exactly one menu on screen. Every screen registers while it is alive and calls
    /// <see cref="CloseOthers"/> the instant it opens, so a second card can never stack on the first:
    /// one closes, the other opens.
    ///
    /// Closing is not always instant — the camera flies back over about a second first — so a screen
    /// on its way out asks <see cref="RoomTakenOver"/> before it hands the room back. When a new menu
    /// has already taken the HUD, the camera and Finik, the old one must leave all three alone.
    /// </summary>
    public static class FinikScreens
    {
        static readonly List<IFinikScreen> screens = new();
        static bool cascading;

        public static void Register(IFinikScreen screen)
        {
            if (screen == null || screens.Contains(screen)) return;
            screens.Add(screen);
        }

        public static void Unregister(IFinikScreen screen) => screens.Remove(screen);

        /// <summary>
        /// Closes every other open menu. Called by a screen right after it marks itself open, so the
        /// screens being closed can already see that someone else owns the room.
        /// </summary>
        public static void CloseOthers(IFinikScreen opening)
        {
            // A Close() that opens something else must not start a second cascade underneath this one.
            if (cascading) return;
            cascading = true;
            try
            {
                // Backwards: a screen may unregister itself as it closes.
                for (int i = screens.Count - 1; i >= 0; i--)
                {
                    if (i >= screens.Count) continue;
                    var screen = screens[i];
                    if (Gone(screen))
                    {
                        screens.RemoveAt(i);
                        continue;
                    }
                    if (ReferenceEquals(screen, opening) || !screen.IsOpen) continue;
                    screen.Close();
                }
            }
            finally
            {
                cascading = false;
            }
        }

        /// <summary>True while any registered game screen is visibly open.</summary>
        public static bool AnyOpen
        {
            get
            {
                foreach (var screen in screens)
                    if (!Gone(screen) && screen.IsOpen) return true;
                return false;
            }
        }

        /// <summary>
        /// True when a menu other than <paramref name="self"/> has taken the room: it owns the HUD,
        /// the camera and Finik, and <paramref name="self"/> must not hand any of them back.
        /// </summary>
        public static bool RoomTakenOver(IFinikScreen self)
        {
            foreach (var screen in screens)
            {
                if (Gone(screen) || ReferenceEquals(screen, self)) continue;
                if (screen.IsOpen && screen.HoldsRoom) return true;
            }
            return false;
        }

        /// <summary>A destroyed MonoBehaviour is not null through an interface; ask Unity instead.</summary>
        static bool Gone(IFinikScreen screen) => screen == null || (screen is Object o && !o);
    }
}
