using Finik.Core;
using Finik.UI.Onboarding;

namespace Finik.UI.Quests
{
    public static class FinikQuestText
    {
        public static string Resolve(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string name = FinikProfileStore.TryLoad(out var profile) ? profile.petName : "Финик";
            string goal = FinikGame.HasSelectedGoal ? FinikGame.Goal.Title : "цель";
            return value.Replace("*Имя персонажа*", name)
                .Replace("*Цель*", goal)
                .Replace("*Покупки*", "«Покупки»");
        }
    }
}
