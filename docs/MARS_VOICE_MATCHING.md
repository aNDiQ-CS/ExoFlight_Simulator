# Сопоставление озвучки Марса — 17.11.2026

Обработаны 205 исходных FLAC (около 18 минут). Подготовлены 71 WAV — все реплики сценария, в том числе 55 для демо.
Исходная папка «Давид Игра» и проект Reaper не изменялись. Распознавание выполнено локально.

Готовые записи: `ExoFlight/Assets/MarsMission/Voice/Audio`. Формат: WAV PCM, 48 кГц, 24 бит, моно.
Unity подключит их существующим MarsVoiceImporter после импорта при следующем открытии проекта. Работа в Play Mode с новой озвучкой пока не проверена: Unity закрыт.

Выбран последний полный дубль нужной реплики по числовому номеру. При повторе внутри файла выбрано последнее полное произнесение.
Границы взяты из монтажного проекта Reaper с небольшим запасом по краям; для S1-01 сохранена вся реплика №19.
Удалены паузы вне реплик и разговоры между дублями. Громкость, тембр и скорость не менялись. Обработка шумов не выполнялась.

## Особые случаи

- **S1-01:** №19 — последний полный дубль. №20 содержит только заключение «Газ добавляйте плавно» и сам по себе не заменяет реплику целиком.
- **EV-10:** выбран №140. №141–142 содержат шутку с другим текстом и не подходят для предупреждения о крене.
- **EV-07:** выбран последний законченный вариант №200: «Ты потерял дрон. Ничего, с новичками бывает. Запускаем следующий». Сценарий допускает несколько вариантов. Дословная версия сценария есть в №197; другая версия — №198.
- Импровизации №151–154 и №205 не подключены к сюжетным репликам. Они остались в исходной папке.
- Имена файлов S1-08 и S6-08 заканчиваются на `_cmd`, хотя в поле «Автор» указан бортовой ИИ. Сохранены имена каталога, чтобы не сломать привязки.

## Проверить дикцию

Это неоднозначности автоматического распознавания, а не доказанные ошибки актёра. Соответствие обеих записей нужным репликам установлено.

- **S1-07, дубль №30:** Проверить на слух слово «Снижаетесь»: распознаётся также как «Снижайтесь». Нужная реплика установлена, выбран последний дубль №30. [Открыть запись](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s1_07_ai.wav).
- **S5-03, дубль №96:** Проверить на слух слово «Снизьте»: распознаётся как «Снести/Снисьте». Нужная реплика установлена, выбран последний дубль №96. [Открыть запись](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s5_03_ai.wav).

## Выбранные записи

| ID | Дубль | Готовый файл | Реплика по сценарию |
|---|---:|---|---|
| S0-01 | 6 | [vo_s0_01_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s0_01_ai.wav) | Бортовой ИИ на связи. Системы в норме, заряд — сто процентов. |
| S0-02 | 11 | [vo_s0_02_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s0_02_ai.wav) | Если закружится голова — скажите оператору, полёт сразу остановят. |
| S0-03 | 159 | [vo_s0_03_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s0_03_cmd.wav) | Оператор, на связи, база. В восьмистах метрах к востоку застрял марсоход, а к району идёт пылевая буря. Забираем его и ведём на точку посадки. |
| S0-04 | 17 | [vo_s0_04_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s0_04_ai.wav) | Взлёт через три… два… один… |
| S0-05 | 164 | [vo_s0_05_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s0_05_cmd.wav) | Взлёт разрешаю. Удачи. |
| S1-01 | 19 | [vo_s1_01_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s1_01_ai.wav) | Гравитация здесь — тридцать восемь процентов земной, но воздух почти не держит. Газ добавляйте плавно. |
| S1-02 | 21 | [vo_s1_02_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s1_02_ai.wav) | Левый стик вверх — набор высоты. |
| S1-03 | 22 | [vo_s1_03_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s1_03_ai.wav) | Правый стик наклоняет дрон. Куда наклоните — туда и полетите. |
| S1-04 | 23 | [vo_s1_04_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s1_04_ai.wav) | Левый стик влево и вправо — разворот на месте. |
| S1-05 | 27 | [vo_s1_05_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s1_05_ai.wav) | Зависание стабильное. Хорошо получается. |
| S1-06 | 168 | [vo_s1_06_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s1_06_cmd.wav) | Наблюдаем маркер на востоке. Держим курс на него. |
| S1-07 | 30 | [vo_s1_07_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s1_07_ai.wav) | Снижаетесь слишком быстро. Добавьте газ. |
| S1-08 | 34 | [vo_s1_08_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s1_08_cmd.wav) | Вы уходите в сторону. Марсоход на востоке. |
| S2-01 | 36 | [vo_s2_01_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_01_ai.wav) | Фиксирую фронт пылевой бури на северо-западе. Скорость ветра растёт. |
| S2-02 | 172 | [vo_s2_02_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_02_cmd.wav) | Срочно, забираем груз и уходим к каньону. |
| S2-03 | 37 | [vo_s2_03_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_03_ai.wav) | Боковой порыв. Держите курс. |
| S2-04 | 45 | [vo_s2_04_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_04_ai.wav) | Марсоход в пятидесяти метрах. Снижайтесь и зависните над ним. |
| S2-05 | 47 | [vo_s2_05_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_05_ai.wav) | Чуть левее. |
| S2-06 | 48 | [vo_s2_06_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_06_ai.wav) | Чуть правее. |
| S2-07 | 49 | [vo_s2_07_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_07_ai.wav) | Чуть вперёд. |
| S2-08 | 50 | [vo_s2_08_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_08_ai.wav) | Чуть назад. |
| S2-09 | 51 | [vo_s2_09_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_09_ai.wav) | Ниже. |
| S2-10 | 53 | [vo_s2_10_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_10_ai.wav) | Держите… Держите… |
| S2-11 | 175 | [vo_s2_11_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s2_11_cmd.wav) | Минута до бури. Надо торопиться. |
| S3-01 | 57 | [vo_s3_01_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s3_01_ai.wav) | Захват подтверждаю. Масса плюс десять и шесть десятых килограмма. |
| S3-02 | 60 | [vo_s3_02_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s3_02_ai.wav) | Расход энергии вырос в полтора раза. Набирайте высоту плавно. |
| S3-03 | 177 | [vo_s3_03_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s3_03_cmd.wav) | Есть груз! Теперь уходим к каньону. |
| S3-04 | 67 | [vo_s3_04_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s3_04_ai.wav) | Видимость падает. Маяки базы не видно, перехожу на инерциальную навигацию. |
| S3-05 | 68 | [vo_s3_05_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s3_05_ai.wav) | Сильный порыв справа! |
| S3-06 | 69 | [vo_s3_06_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s3_06_ai.wav) | Сильный порыв слева! |
| S3-07 | 71 | [vo_s3_07_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s3_07_ai.wav) | Груз раскачивается. Не дёргайте стики. |
| S3-08 | 180 | [vo_s3_08_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s3_08_cmd.wav) | До каньона осталось совсем немного. |
| S3-09 | 75 | [vo_s3_09_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s3_09_ai.wav) | Груз потерян! Вернитесь и подберите его. |
| S4-01 | 182 | [vo_s4_01_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s4_01_cmd.wav) | Ныряем в каньон! |
| S4-02 | 77 | [vo_s4_02_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s4_02_ai.wav) | Ветер ослаб. Воздух здесь плотнее — тяга немного выросла. |
| S4-03 | 81 | [vo_s4_03_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s4_03_ai.wav) | За бортом минус восемьдесят. Подогрев батареи забирает заряд. |
| S4-04 | 84 | [vo_s4_04_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s4_04_ai.wav) | Радиационный фон растёт — вспышка на Солнце. Рекомендую укрытие. |
| S4-05 | 188 | [vo_s4_05_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s4_05_cmd.wav) | Видим лавовую трубку. Это единственное укрытие. |
| S4-06 | 85 | [vo_s4_06_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s4_06_ai.wav) | Стена близко! |
| S4-07 | 87 | [vo_s4_07_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s4_07_ai.wav) | Осторожно, скала! |
| S4-08 | 89 | [vo_s4_08_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s4_08_ai.wav) | Радиация растёт. Не задерживайтесь. |
| S5-01 | 93 | [vo_s5_01_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s5_01_ai.wav) | Связь с базой потеряна. Включаю фары. |
| S5-02 | 190 | [vo_s5_02_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s5_02_cmd.wav) | …держимся середины… выход через двести метров… |
| S5-03 | 96 | [vo_s5_03_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s5_03_ai.wav) | Тоннель сужается. Снизьте скорость. |
| S5-04 | 99 | [vo_s5_04_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s5_04_ai.wav) | Странно. Датчики фиксируют поток воздуха из глубины тоннеля. |
| S5-05 | 101 | [vo_s5_05_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s5_05_ai.wav) | Впереди развилка. К выходу ведёт правый проход. |
| S5-06 | 103 | [vo_s5_06_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s5_06_ai.wav) | Вижу свет. Выход прямо по курсу. |
| S6-01 | 105 | [vo_s6_01_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s6_01_ai.wav) | Связь восстановлена. Буря позади. |
| S6-02 | 193 | [vo_s6_02_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s6_02_cmd.wav) | Видим птичку на радаре. Площадка прямо по курсу. |
| S6-03 | 106 | [vo_s6_03_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s6_03_ai.wav) | Высота десять метров. |
| S6-04 | 107 | [vo_s6_04_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s6_04_ai.wav) | Пять. |
| S6-05 | 108 | [vo_s6_05_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s6_05_ai.wav) | Два. |
| S6-06 | 109 | [vo_s6_06_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s6_06_ai.wav) | Касание. |
| S6-07 | 110 | [vo_s6_07_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s6_07_ai.wav) | Груз отцеплен. Двигатели выключены. |
| S6-08 | 113 | [vo_s6_08_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s6_08_cmd.wav) | Марсоход на месте. Отличная работа, оператор. |
| S6-09 | 196 | [vo_s6_09_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_s6_09_cmd.wav) | Это рекорд смены! Записываю тебя в лучшие пилоты базы. |
| EV-01 | 115 | [vo_ev_01_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_01_ai.wav) | Заряд тридцать процентов. |
| EV-02 | 118 | [vo_ev_02_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_02_ai.wav) | Заряд пятнадцать процентов. Выбирайте кратчайший путь. |
| EV-03 | 121 | [vo_ev_03_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_03_ai.wav) | Критический заряд! Немедленно на посадку. |
| EV-04 | 123 | [vo_ev_04_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_04_ai.wav) | Удар корпусом. Повреждений нет. |
| EV-05 | 126 | [vo_ev_05_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_05_ai.wav) | Повреждение винта. Тяга снижена. |
| EV-06 | 131 | [vo_ev_06_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_06_ai.wav) | Аппарат потерян. |
| EV-07 | 200 | [vo_ev_07_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_07_cmd.wav) | Дрон потерян. Ничего, оператор, — запускаем резервный. |
| EV-08 | 136 | [vo_ev_08_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_08_ai.wav) | Восстановление с последней точки маршрута. |
| EV-09 | 204 | [vo_ev_09_cmd.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_09_cmd.wav) | Буря накрыла маршрут. Возвращайся, попробуем ещё раз. |
| EV-10 | 140 | [vo_ev_10_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_10_ai.wav) | Крен слишком большой. Выравниваю. |
| EV-11 | 143 | [vo_ev_11_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_11_ai.wav) | Выше подниматься нельзя: там слишком сильный ветер. |
| EV-12 | 145 | [vo_ev_12_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_12_ai.wav) | Вы удаляетесь от цели. |
| EV-13 | 148 | [vo_ev_13_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ev_13_ai.wav) | Миссия не выполнена. |
| AR-01 | 149 | [vo_ar_01_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ar_01_ai.wav) | Время сессии заканчивается. Заходите на посадку. |
| AR-02 | 150 | [vo_ar_02_ai.wav](../ExoFlight/Assets/MarsMission/Voice/Audio/vo_ar_02_ai.wav) | Итоги полёта на экране. |

## Проверка и происхождение

Все 71 экспортированный файл повторно распознаны. Проверены уникальность имён, наличие всех ID, формат, длительность, непустой сигнал и точное копирование файлов в проект.
Это проверка содержания через распознавание речи и монтажные данные, не ручное прослушивание каждого дубля.
`mars-voice-selection.json` содержит исходный файл, номер дубля, границы фрагмента, распознанный текст и SHA-256 для каждого результата.
`mars-voice-transcripts.json` сохраняет машинную расшифровку всех 205 исходников, включая прерванные записи и импровизации.
