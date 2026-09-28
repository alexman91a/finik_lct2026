using System.Collections.Generic;

namespace Finik.UI
{
    public readonly struct FinikGlossaryEntry
    {
        public readonly string Term;
        public readonly string Definition;
        public readonly string Example;

        public FinikGlossaryEntry(string term, string definition, string example)
        {
            Term = term;
            Definition = definition;
            Example = example;
        }
    }

    public static class FinikGlossary
    {
        static readonly FinikGlossaryEntry[] Entries =
        {
            new("Бюджет", "План, как распределить свои деньги, чтобы хватило на важное и осталось на желания.",
                "Например: часть монет — на «Нужно», часть — на «Хочу», часть — в копилку."),
            new("Доход", "Деньги, которые ты получаешь.", "В игре доход — это монеты, которые появляются за задания и другие награды."),
            new("Нужно", "То, без чего трудно обойтись или что важно сделать в первую очередь.",
                "Еда для питомца — это «Нужно»."),
            new("Хочу", "То, что приятно иметь, но можно купить позже.", "Новая игрушка или украшение — это «Хочу»."),
            new("Накопления", "Деньги, которые ты не тратишь сейчас, а откладываешь на будущее.",
                "Монеты в копилке — это твои накопления."),
            new("Цель", "То, на что ты решил накопить деньги.", "Если цель стоит 100 монет, можно понемногу откладывать до 100."),
            new("Цена", "Сколько денег нужно отдать за вещь.", "Перед покупкой сравни цену с количеством монет у тебя."),
            new("Баланс", "Сколько свободных денег у тебя есть прямо сейчас.", "Если баланс 20, ты можешь потратить не больше 20 монет."),
            new("План", "Как ты решил распорядиться деньгами заранее.", "Сначала можно запланировать 10 монет на нужное и 5 в копилку."),
            new("Факт", "Что получилось на самом деле после покупок и накоплений.",
                "Если планировал отложить 5 и действительно отложил 5 — план и факт совпали.")
        };

        public static IReadOnlyList<FinikGlossaryEntry> All => Entries;
        public static int Count => Entries.Length;
        public static FinikGlossaryEntry At(int index) => Entries[index < 0 ? 0 : index >= Entries.Length ? Entries.Length - 1 : index];
    }
}
