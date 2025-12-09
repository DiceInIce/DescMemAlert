# MemAlerts

Сетевое приложение для отправки видео-алертов (мемов) на экраны друзей. Клиент — WPF (.NET 9), сервер — ASP.NET Core SignalR (.NET 9) с PostgreSQL + Dapper.

## 🚀 Быстрый старт

### 1) Требования
- .NET 9 SDK
- PostgreSQL 14+ (порт по умолчанию 5432)

Создайте БД и пользователя (пример):
```sql
create database memalerts;
create user appuser with password 'StrongPass';
grant all privileges on database memalerts to appuser;
```

Создайте таблицы:
```sql
create table users (
  id text primary key,
  login text not null unique,
  email text not null unique,
  password_hash text not null,
  created_at timestamptz not null default now()
);

create table friendships (
  id text primary key,
  user_id1 text not null references users(id) on delete cascade,
  user_id2 text not null references users(id) on delete cascade,
  user_login1 text not null,
  user_login2 text not null,
  status int not null,              -- 0=Pending, 1=Accepted, 2=Rejected
  requester_id text not null references users(id) on delete cascade,
  created_at timestamptz not null default now(),
  accepted_at timestamptz
);

create index idx_friendships_user1 on friendships(user_id1);
create index idx_friendships_user2 on friendships(user_id2);
create index idx_users_login_lower on users((lower(login)));
create index idx_users_email_lower on users((lower(email)));
```

### 2) Настройка

#### Сервер `MemAlerts.Server/config.json`
```json
{
  "ServerIp": "0.0.0.0",
  "ServerPort": 5050,
  "ConnectionStrings": {
    "PostgreSql": "Host=127.0.0.1;Port=5432;Database=memalerts;Username=appuser;Password=StrongPass;Pooling=true"
  }
}
```
На проде лучше задавать connection string через переменную окружения `ConnectionStrings__PostgreSql`.

#### Клиент `MemAlerts.Client/config.json`
```json
{
  "ServerIp": "127.0.0.1",
  "ServerPort": 5050,
  "WebViewUserAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ...",
  "YoutubeAndroidUserAgent": "Mozilla/5.0 (Linux; Android 11; Pixel 5 ...)",
  "LocalWebServerPort": 5055
}
```
Укажите `ServerIp`/`ServerPort` вашего сервера (VPS).

### 3) Запуск сервера
```bash
cd MemAlerts.Server
dotnet run
```
Слушает `http://*:5050` и хостит SignalR-хаб `/alerthub`.

### 4) Запуск клиента
```bash
cd MemAlerts.Client
dotnet run
```
В окне логина: введите логин/email и пароль, при необходимости зарегистрируйтесь.

## 🛠 Технологии
- **Client:** WPF, MVVM, WebView2
- **Server:** ASP.NET Core SignalR, Serilog, Dapper, PostgreSQL
- **Shared:** общие модели (`MemAlerts.Shared`)

