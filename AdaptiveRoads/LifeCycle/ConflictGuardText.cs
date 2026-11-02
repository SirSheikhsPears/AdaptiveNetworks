namespace AdaptiveRoads.LifeCycle {
    using System.Collections.Generic;

    internal static class ConflictGuardText {
        internal const string AutoLoadStoppedLine =
            "So direct save loading was broken off — but nothin’s really broken.";
        internal sealed class Message {
            internal string Title;
            internal readonly List<string> Paragraphs = new List<string>();
        }

        // Braces mark only approved emphasis spans; they are removed before rendering.
        internal static Message Create(bool conflict, ConflictGuard.DisableResult disable,
            ConflictGuardDependency.Status tm, bool editor, bool interrupted) {
            string shortName = UserMod.GuardShortName;
            string fullName = UserMod.GuardFullName;
            bool disabled = disable == ConflictGuard.DisableResult.Disabled;
            bool warning = ConflictGuardDependency.IsWarning(tm);
            var message = new Message();
            message.Title = shortName + " \u2014 " + (conflict ? "Mod Conflict"
                : tm == ConflictGuardDependency.Status.NotDetected ? "TM:PE Not Found"
                : tm == ConflictGuardDependency.Status.Disabled ? "TM:PE Disabled"
                : "TM:PE Integration Issue");
            if (conflict && tm == ConflictGuardDependency.Status.Disabled)
                message.Title += " | TM:PE Disabled";
            else if (conflict && tm == ConflictGuardDependency.Status.NotDetected)
                message.Title += " | TM:PE Not Found";
            if (conflict) {
                string cause = ConflictGuard.CurrentANCompetitor ==
                    ConflictGuard.ANCompetitorPresence.IncludedDisabled
                    ? "Another version of Adaptive Networks is included but disabled in Content Manager."
                    : "Another version of Adaptive Networks is enabled.";
                if (tm == ConflictGuardDependency.Status.Disabled) cause += " {TM:PE is disabled.}";
                else if (tm == ConflictGuardDependency.Status.NotDetected) cause += " {TM:PE was not detected.}";
                else if (tm == ConflictGuardDependency.Status.ConnectionProblem)
                    cause += " {" + shortName + "} detected a problem with {TM:PE}.";
                message.Paragraphs.Add(cause);
                message.Paragraphs.Add(!disabled ? "{" + shortName + "} could not be disabled automatically."
                    : warning ? "{" + shortName + "} has been disabled to prevent conflicts."
                    : "To prevent conflicts, {" + fullName + "} has been disabled.");
                message.Paragraphs.Add("However some Adaptive Networks roads may load without their additional properties (flags), potentially affecting their appearance and behavior.");
            } else if (tm == ConflictGuardDependency.Status.NotDetected) {
                message.Paragraphs.Add("{" + fullName + "} {could not detect TM:PE}, which is required for some network features.");
            } else if (tm == ConflictGuardDependency.Status.Disabled) {
                message.Paragraphs.Add("{" + fullName + "} detected that {TM:PE is disabled.}");
                message.Paragraphs.Add("{Don't panic!} Just some network features may not work correctly, so ;)");
            } else {
                message.Paragraphs.Add("{" + shortName + "} detected a problem with {TM:PE}. So, some network features may not work correctly.");
            }
            if (interrupted && message.Paragraphs.Count > 0) {
                // Insert after the first sentence, outside a closing emphasis brace.
                string first = message.Paragraphs[0];
                int period = first.IndexOf('.');
                while (period > 0 && period + 1 < first.Length &&
                    char.IsDigit(first[period - 1]) && char.IsDigit(first[period + 1]))
                    period = first.IndexOf('.', period + 1);
                int boundary = period < 0 ? first.Length : period + 1;
                while (boundary < first.Length && first[boundary] == '}') boundary++;
                string tail = first.Substring(boundary).TrimStart();
                message.Paragraphs[0] = first.Substring(0, boundary) + "\n" +
                    AutoLoadStoppedLine +
                    (tail.Length > 0 ? "\n" + tail : "");
            }
            string action = "";
            if (tm == ConflictGuardDependency.Status.NotDetected)
                action = "Check that {TM:PE is installed and enabled} in Content Manager, then restart the game.";
            else if (tm == ConflictGuardDependency.Status.Disabled)
                action = "{Enable TM:PE} in Content Manager and restart the game.";
            else if (tm == ConflictGuardDependency.Status.ConnectionProblem)
                action = "Make sure both mods are up to date. Still broken? My bad, brain.exe crashed - forgot to update ANR...  If the problem persists, {report it on the ANR Workshop page. Please ;)}";
            if (conflict) {
                if (ConflictGuard.CurrentANCompetitor ==
                    ConflictGuard.ANCompetitorPresence.IncludedDisabled)
                    action = warning
                        ? "{But don't panic!} Exclude the other Adaptive Networks version from your playset. " + action
                        : "{But don't panic!} Exclude the other Adaptive Networks version from your playset, then restart the game.";
                else
                    action = warning ? "{But don't panic!} Just keep one Adaptive Networks version enabled. " + action
                        : "{But don't panic!} Just keep one version of Adaptive Networks enabled, then restart the game.";
            }
            message.Paragraphs.Add(action);
            message.Paragraphs.Add("{Quit to desktop.}");
            string save = editor ? "Do not save this session now!" : "Do not save your city now!";
            message.Paragraphs.Add(conflict ? "{" + save + "}" : "{" + save + "} (until the issue is resolved).");
            return message;
        }
    }
}
