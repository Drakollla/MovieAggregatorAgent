# MovieAgent (Work in Progress)

**MovieAggregator** — ассистент, предназначенный для поиска фильмов, генерации рекомендаций и планирования просмотров. 
Проект использует локальную нейросеть (Ollama) и Microsoft Semantic Kernel для оркестрации вызова инструментов (Tool Calling).

## Текущий стек технологий
* **.NET 8 (C# 12)** / Console Application (в планах миграция на Telegram Bot)
* **Microsoft Semantic Kernel**
* **Ollama** (Локальная модель Qwen 2.5 3B)
* **Kinopoisk API v1.4**

## Реализованные фичи (Agent Skills)
* `SearchMovie` & `SearchByCriteria` — поиск сюжетов и фильтрация фильмов.
* `FindSimilar` — поиск похожих фильмов на основе графов рекомендаций.
* `CalendarPlanner` — генерация `.ics` / Google Calendar ссылок для планирования просмотров. Агент вычисляет текущую дату, получает длительность фильма из базы и планирует время события.

## Запуск
1. Установите [Ollama](https://ollama.com/) и скачайте модель: `ollama pull qwen2.5:3b`.
2. Добавьте ваш ключ Кинопоиска в User Secrets
