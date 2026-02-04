# Prime Architecture Mediator 🚀

Uma biblioteca leve e poderosa para implementação do padrão Mediator em .NET, oferecendo duas opções de implementação baseadas em performance e desacoplamento.

## 📦 Instalação e Configuração

Escolha o namespace que melhor se adapta às suas necessidades e registre no seu `Program.cs`:

### Opção A: Standard (Equilíbrio entre Pureza e Performance)
Ideal para 95% das aplicações. Mantém seus DTOs limpos.
```csharp
using Prime.Architecture.Mediator.Standard;

builder.Services.AddMediatorStandard(typeof(Program).Assembly);
```

### Opção B: HighPerformance (Zero Reflection)
Ideal para sistemas de ultra-baixa latência.
```csharp
using Prime.Architecture.Mediator.HighPerformance;

builder.Services.AddMediatorHighPerformance(typeof(Program).Assembly);
```

---

## 🛠️ Exemplos de Uso

### 1. Requests (Comandos e Consultas)

#### No modo Standard:
```csharp
using Prime.Architecture.Mediator.Standard.Requests;

// O Request é um DTO puro
public record GetUserQuery(int Id) : IRequest<string>;

public class GetUserHandler : IRequestHandler<GetUserQuery, string>
{
    public Task<string> Handle(GetUserQuery request, CancellationToken ct)
    {
        return Task.FromResult($"Usuário {request.Id}");
    }
}
```

#### No modo HighPerformance:
```csharp
using Prime.Architecture.Mediator.HighPerformance.Requests;

public record GetUserQuery(int Id) : IRequest<string>
{
    // Requer a implementação do método SendTo para evitar Reflection
    public Task<string> SendTo(Mediator mediator, CancellationToken ct) 
        => mediator.HandleInternal<GetUserQuery, string>(this, ct);
}
```

---

### 2. Notificações (Eventos)
O funcionamento é idêntico em ambos os namespaces.

```csharp
using Prime.Architecture.Mediator.Standard.Notifications; // ou HighPerformance

public record UserCreated(string Name) : INotification;

public class EmailHandler : INotificationHandler<UserCreated>
{
    public Task Handle(UserCreated notification, CancellationToken ct)
    {
        Console.WriteLine($"E-mail enviado para {notification.Name}");
        return Task.CompletedTask;
    }
}
```

---

### 3. Pipelines (Middlewares)
Perfeito para Logs, Validação ou Transações.

```csharp
using Prime.Architecture.Mediator.Standard.Pipeline;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        Console.WriteLine($"Iniciando request {typeof(TRequest).Name}");
        var response = await next();
        Console.WriteLine($"Finalizado request {typeof(TRequest).Name}");
        return response;
    }
}
```

---

## 🚀 Como Executar

Injete o `Mediator` no seu controlador ou serviço e utilize os métodos `Send` ou `Publish`.

```csharp
public class UserController(Mediator mediator)
{
    public async Task Get(int id)
    {
        // Enviar uma Request (espera retorno)
        var result = await mediator.Send(new GetUserQuery(id));

        // Publicar uma Notificação (dispare e esqueça)
        await mediator.Publish(new UserCreated("Igor"));
    }
}
```

## 📊 Comparativo Técnico

| Característica | Standard | HighPerformance |
| :--- | :--- | :--- |
| **Reflection** | Uma única vez (no cache) | **Zero** |
| **Simplicidade** | Alta (DTOs puros) | Média (exige método no Request) |
| **Pipelines** | Sim | Sim |
| **Indicado para** | Web APIs, Microserviços | Sistemas de High-Trading, Jogos |

---
*Desenvolvido com foco em escalabilidade e baixo consumo de recursos.*
