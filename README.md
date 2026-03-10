# LocalChatBotApi

ASP.NET backend responsible for managing the application layer of the local AI chatbot platform.

This service acts as the **gateway between the frontend and the AI agent service**.

---

# Responsibilities

* user management
* chat session creation
* conversation persistence
* message storage
* communication with the AI agent service
* streaming AI responses to the frontend

---

# Architecture Role

User

↓

Angular Frontend

↓

LocalChatBotApi

↓

AI Agent Service

The backend handles all application data while delegating AI reasoning to the Python service.

---

# Technologies

* ASP.NET
* C#
* SQL Server
* Server Sent Events (SSE)

---

# Features

* chat session persistence
* message history
* streaming AI responses
* integration with external AI services
