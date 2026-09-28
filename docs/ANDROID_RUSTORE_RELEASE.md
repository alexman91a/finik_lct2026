# Android: сборка и подпись

## Окружение

- Unity `6000.6.1f1` с Android Build Support, SDK/NDK и OpenJDK.
- Проект: [`unity/Finik`](../unity/Finik); сцена: `Assets/Finik/Scenes/FinikRoomNavigationPrototype.unity`.
- APK: `com.finik.game`, версия `0.1.2` (versionCode `3`), Android 8.0/API 26+, ARM64, IL2CPP, OpenGLES3.

## Воспроизводимая сборка

1. Открыть проект в Unity `6000.6.1f1` и выбрать платформу Android в **File → Build Profiles**.
2. Выполнить **Finik → Build → Build Android APK**. Unity сохраняет `unity/builds/android-apk/Finik.apk` и `build_report.txt`.
3. Для релизного файла подписать APK собственным ключом вне репозитория:

```text
apksigner sign --out Finik-release.apk --ks <путь-к-keystore> --ks-key-alias <alias> Finik.apk
apksigner verify --verbose --print-certs Finik-release.apk
```

Пароли вводятся интерактивно; ключ, пароли и их резервные копии не входят в Git. Для обновлений Android применяется тот же сертификат. Сборщик Unity без внешнего шага подписи создаёт APK с ключом разработки.

## Релизный APK

| Параметр | Значение |
| --- | --- |
| APK | `Finik-release.apk` |
| SHA-256 | `C0CA0CFD6EDA084C92814234E354F025E2B4FFE5E40EF276FB24C4C661F6A7F9` |
| Подпись | Один подписант, `CN=Finik`, схемы v2/v3 |
| SHA-256 сертификата | `02FCCA0B9F96F42A9657EB5EB84965FF823A9C4EA9EC233F88C86D11ADCC59E5` |

Состав разрешений и данные профиля описаны в [отдельном разделе](ANDROID_PERMISSIONS_AUDIT.md); результаты проверки сборки — в [отчёте](ANDROID_QA_REPORT.md).
