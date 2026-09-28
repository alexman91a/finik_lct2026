<p align="center">
  <img src="unity/Finik/Assets/Finik/UI/Art/ui_logo.png" width="320" alt="Финик">
</p>

<h1 align="center">Финик</h1>
<p align="center"><strong>Финансовая грамотность через заботу о виртуальном питомце</strong></p>
<p align="center">Мобильная игра для детей 7–11 лет · задача Департамента финансов города Москвы · ЛЦТ 2026</p>

Ребёнок получает игровые монеты, заранее делит их между обязательными расходами, желаниями и накоплениями, совершает покупки и видит последствия решений для бюджета и питомца. Приложение работает с локальным профилем без регистрации, реальных платежей и рекламы.

## Игра на экране

<table>
  <tr><th>Горизонтальный режим</th><th>Вертикальный режим</th></tr>
  <tr>
    <td><strong>Комната и питомец</strong><br><a href="docs/expert/screenshots-landscape/10-room.jpg"><img src="docs/expert/screenshots-landscape/10-room.jpg" width="650" alt="Комната с питомцем в горизонтальном режиме"></a></td>
    <td><strong>Комната и питомец</strong><br><a href="docs/rustore/screenshots/01-room.jpg"><img src="docs/rustore/screenshots/01-room.jpg" width="205" alt="Комната с питомцем в вертикальном режиме"></a></td>
  </tr>
  <tr>
    <td><strong>Финансовые задания</strong><br><a href="docs/expert/screenshots-landscape/06-quests.jpg"><img src="docs/expert/screenshots-landscape/06-quests.jpg" width="650" alt="Задания в горизонтальном режиме"></a></td>
    <td><strong>Финансовые задания</strong><br><a href="docs/rustore/screenshots/02-quests.jpg"><img src="docs/rustore/screenshots/02-quests.jpg" width="205" alt="Задания в вертикальном режиме"></a></td>
  </tr>
  <tr>
    <td><strong>Копилка и цель</strong><br><a href="docs/expert/screenshots-landscape/12-savings.jpg"><img src="docs/expert/screenshots-landscape/12-savings.jpg" width="650" alt="Копилка в горизонтальном режиме"></a></td>
    <td><strong>Копилка и цель</strong><br><a href="docs/rustore/screenshots/04-savings.jpg"><img src="docs/rustore/screenshots/04-savings.jpg" width="205" alt="Копилка в вертикальном режиме"></a></td>
  </tr>
</table>

[Видео игрового цикла](docs/expert/Finik-demo.mp4) · [Все экраны](docs/expert/README.md) · [Материалы RuStore](docs/rustore/CARD.md)

## Android APK

| Сборка | Данные |
| --- | --- |
| [Скачать подписанный APK](https://github.com/alexman91a/finik_lct2026/releases/download/v0.1.2/Finik-release.apk) | `Finik-release.apk`, Android 8.0+, ARM64 |
| Пакет и версия | `com.finik.game`, `0.1.2` (`versionCode 3`) |
| SHA-256 APK | `C0CA0CFD6EDA084C92814234E354F025E2B4FFE5E40EF276FB24C4C661F6A7F9` |
| Подпись | Релизный сертификат Finik, APK Signature Scheme v2/v3 |

[Проверка APK](docs/ANDROID_QA_REPORT.md) и [сборка Android](docs/ANDROID_RUSTORE_RELEASE.md) описаны в документации.

[Карточка RuStore](docs/rustore/CARD.md) содержит описание, иконку и скриншоты приложения.

## Игровой цикл

```text
Питомец и стартовый бюджет → план «Нужно / Хочу / Коплю»
→ задания, покупки и накопления → план и факт → новый период → рост питомца
```

Первый период начинается с 30 игровых монет. Ознакомительное задание использует этот бюджет и не даёт второй награды; следующие задания начисляют монеты по правилам игры. Баланс не становится отрицательным, а каждое начисление и списание отражается в истории.

**Состав игры:** пять последовательных игровых периодов без календарного ожидания, 15 финансовых заданий, 24 товара обязательной и необязательной категории, четыре цели накопления, варианты питомца и три стадии его развития. [Матрица ТЗ](docs/REQUIREMENTS_TRACEABILITY.md) связывает обязательные функции с кодом; [сценарий](docs/COMPETITION_FLOW.md) повторяет Приложение А.

## Дополнительные возможности

- **Три локальные мини-игры:** «Найди пару», «Три в ряд» и «Лови монетки». Они развивают внимание, дополняют уход за питомцем и сохраняют игровой прогресс.
- **3D-комната и анимированный питомец:** ребёнок видит реакции персонажа на действия и развитие после серии финансовых решений.
- **Озвученные подсказки, музыка и эффекты:** голосовые реплики сопровождают знакомство, задания и действия в комнате; музыку, эффекты и голос можно отключить отдельно.
- **Персонализация:** выбор питомца, аксессуары и темы оформления.
- **Интерфейс для детей 7–11 лет:** короткие инструкции, крупные действия и объяснение результата после выбора. Игровые сценарии проверены с детьми целевой группы.

## Исходный проект

| Раздел | Содержание |
| --- | --- |
| [`unity/Finik`](unity/Finik) | Unity-проект: сцена, интерфейс, логика, контент и ресурсы |
| [`assets`](assets) и [`art/blender`](art/blender) | Исходные визуальные, звуковые и 3D-материалы |
| [`docs`](docs/README.md) | Документация, ТЗ и ответы заказчика |

**Запуск в редакторе:** Unity `6000.6.1f1` с Android Build Support. Открыть `unity/Finik` через Unity Hub и запустить сцену `Assets/Finik/Scenes/FinikRoomNavigationPrototype.unity`. Для APK выполнить **Finik → Build → Build Android APK**, затем подписать файл по [инструкции](docs/ANDROID_RUSTORE_RELEASE.md).

**Проверки:** 75 из 75 EditMode-тестов Unity пройдены 28.09.2026. Архитектура, формулы экономики и связь финансовых решений с ростом питомца описаны в [документации](docs/ARCHITECTURE.md).

<sub>Основание: [техническое задание](docs/customer/6.%20Департамент%20финансов%20города%20Москвы.pdf) и [ответы заказчика](docs/customer/Город%206%20-%20Вопросы%20и%20ответы.pdf).</sub>
