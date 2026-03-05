# Prime Architecture

Uma suíte de bibliotecas arquiteturais para .NET, focada em performance, simplicidade e código limpo.

Esta solução contém as seguintes bibliotecas principais:

1. **[Mediator](#-mediator)**: Implementação do padrão Mediator (Standard e HighPerformance).
2. **[Mapper](#-mapper)**: Mapeador de objetos leve e flexível.

---

## 🚀 Mediator

Uma biblioteca leve e poderosa para implementação do padrão Mediator em .NET, oferecendo duas opções de implementação baseadas em performance e desacoplamento.

### 📦 Instalação e Configuração

Escolha o namespace que melhor se adapta às suas necessidades e registre no seu `Program.cs`.

#### Opção A: Standard (Equilíbrio entre Pureza e Performance)
Ideal para 95% das aplicações. Mantém seus DTOs limpos.

**Arquivo: `Program.cs`**
```csharp
using Prime.Architecture.Mediator.Standard;

var builder = WebApplication.CreateBuilder(args);

// Registra o Mediator e todos os Handlers no assembly atual
builder.Services.AddMediatorStandard(typeof(Program).Assembly);

var app = builder.Build();
```

#### Opção B: HighPerformance (Zero Reflection)
Ideal para sistemas de ultra-baixa latência.

**Arquivo: `Program.cs`**
```csharp
using Prime.Architecture.Mediator.HighPerformance;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediatorHighPerformance(typeof(Program).Assembly);

var app = builder.Build();
```

### 🛠️ Guia de Implementação Completo

Abaixo, um exemplo completo de um caso de uso para **Criar Cliente**.

#### 1. Definindo o Request (Comando)

O Request representa a intenção de executar uma ação.

**Arquivo: `Application/Customers/CreateCustomerCommand.cs`**

*Modo Standard:*
```csharp
using Prime.Architecture.Mediator.Standard.Requests;

public record CreateCustomerCommand(string Name, string Email) : IRequest<int>;
```

*Modo HighPerformance:*
```csharp
using Prime.Architecture.Mediator.HighPerformance;
using Prime.Architecture.Mediator.HighPerformance.Requests;

public record CreateCustomerCommand(string Name, string Email) : IRequest<int>
{
    // No modo HighPerformance, o próprio request sabe como se enviar
    public Task<int> SendTo(Mediator mediator, CancellationToken ct) 
        => mediator.HandleInternal<CreateCustomerCommand, int>(this, ct);
}
```

#### 2. Implementando o Handler

O Handler contém a lógica de negócio.

**Arquivo: `Application/Customers/CreateCustomerHandler.cs`**

```csharp
using Prime.Architecture.Mediator.Standard.Requests; // ou HighPerformance.Requests

public class CreateCustomerHandler : IRequestHandler<CreateCustomerCommand, int>
{
    private readonly ICustomerRepository _repository;

    public CreateCustomerHandler(ICustomerRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> Handle(CreateCustomerCommand request, CancellationToken ct)
    {
        var customer = new Customer(request.Name, request.Email);
        await _repository.AddAsync(customer, ct);
        
        return customer.Id;
    }
}
```

#### 3. Utilizando no Controller

Injete o `Mediator` e envie o comando.

**Arquivo: `API/Controllers/CustomersController.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Prime.Architecture.Mediator.Standard; // ou HighPerformance

[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly Mediator _mediator;

    public CustomersController(Mediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerCommand command)
    {
        var id = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        // Exemplo de Query
        var result = await _mediator.Send(new GetCustomerQuery(id));
        return Ok(result);
    }
}
#### 4. Notificações (Eventos)

Use notificações para efeitos colaterais (ex: enviar email, logs), onde múltiplos handlers podem escutar o mesmo evento.

**Arquivo: `Application/Customers/CustomerCreatedEvent.cs`**
```csharp
using Prime.Architecture.Mediator.Standard.Notifications;

public record CustomerCreatedEvent(int CustomerId, string Email) : INotification;
```

**Arquivo: `Application/Notifications/EmailHandler.cs`**
```csharp
using Prime.Architecture.Mediator.Standard.Notifications;

public class EmailHandler : INotificationHandler<CustomerCreatedEvent>
{
    public Task Handle(CustomerCreatedEvent notification, CancellationToken ct)
    {
        Console.WriteLine($"Enviando e-mail de boas-vindas para o cliente {notification.CustomerId} no email {notification.Email}");
        return Task.CompletedTask;
    }
}
```

---

## 🗺️ Mapper

Uma biblioteca de mapeamento de objetos eficiente que suporta configurações via atributos ou profiles fluentes.

### 📦 Configuração e Injeção de Dependência

Recomendamos configurar o Mapper como um Singleton no container de DI.

**Arquivo: `Program.cs`**

```csharp
using Prime.Architecture.Mapper.Configuration;
using Prime.Architecture.Mapper;

var builder = WebApplication.CreateBuilder(args);

// Configuração do Mapper
var mapperConfig = new MapperConfiguration();
mapperConfig.AddProfile<UserProfile>(); // Registre seus profiles aqui
// mapperConfig.AddProfile<ProductProfile>();

// Registra a instância do IMapper
IMapper mapper = mapperConfig.CreateMapper();
builder.Services.AddSingleton(mapper);

var app = builder.Build();
```

### 🛠️ Guia de Implementação Completo

Exemplo de mapeamento de uma Entidade para um DTO (Data Transfer Object).

#### 1. Classes de Origem e Destino

**Arquivo: `Domain/Entities/User.cs`**
```csharp
public class User
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTime BirthDate { get; set; }
    public string EmailAddress { get; set; }
}
```

**Arquivo: `Application/DTOs/UserDto.cs`**
```csharp
public class UserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } // Propriedade calculada
    public int Age { get; set; }         // Propriedade calculada
    public string Email { get; set; }    // Nome diferente da origem
}
```

#### 2. Criando o Profile de Mapeamento

Profiles organizam as regras de transformação.

**Arquivo: `Application/Mappings/UserProfile.cs`**
```csharp
using Prime.Architecture.Mapper.Configuration;
using Prime.Architecture.Mapper.Attributes;

public class UserProfile : MappingProfile
{
    public UserProfile()
    {
        CreateMap<User, UserDto>()
            // Mapeamento customizado: Concatenação de strings
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.FirstName} {src.LastName}"))
            
            // Mapeamento customizado: Cálculo de idade
            // Nota: O método MapFrom aceita uma função lambda para cálculo
            .ForMember(dest => dest.Age, opt => opt.MapFrom(src => DateTime.Now.Year - src.BirthDate.Year))
            
            // Mapeamento de propriedade com nome diferente
            // Nota: Para mapear de uma propriedade específica, use MapFrom com lambda
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.EmailAddress));
    }
}
```

#### 3. Utilizando no Serviço ou Controller

**Arquivo: `API/Controllers/UsersController.cs`**
```csharp
using Microsoft.AspNetCore.Mvc;
using Prime.Architecture.Mapper;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IMapper _mapper;
    private readonly IUserRepository _repository;

    public UsersController(IMapper mapper, IUserRepository repository)
    {
        _mapper = mapper;
        _repository = repository;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var user = await _repository.GetByIdAsync(id);
        
        // Converte a entidade User para UserDto
        var userDto = _mapper.Map<User, UserDto>(user);
        
        return Ok(userDto);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _repository.GetAllAsync();
        
        // Mapeamento de listas
        // Nota: MapList é um método de extensão
        var userDtos = users.MapList<User, UserDto>(_mapper);
        
        return Ok(userDtos);
    }
}
```

### ✨ Funcionalidades Extras

#### Mapeamento via Atributos (Sem Profile)

Para casos simples, você pode anotar diretamente o DTO ou a Entidade sem precisar criar um Profile.

> **Importante**: Mesmo utilizando apenas atributos, você **deve** registrar o `IMapper` no seu container de injeção de dependência (conforme a seção [Configuração](#-configuração-e-injeção-de-dependência)). O `MapperConfiguration` pode ser instanciado sem adicionar nenhum profile.

**1. Atributo `[MapFrom]` (No Destino)**
Use quando você quer mapear uma propriedade do DTO a partir de uma propriedade com nome diferente na origem.

**Arquivo: `Application/DTOs/SimpleUserDto.cs`**
```csharp
using Prime.Architecture.Mapper.Attributes;

public class SimpleUserDto
{
    public string Name { get; set; }
    
    // Busca o valor de "EmailAddress" na classe de origem (ex: User)
    [MapFrom("EmailAddress")] 
    public string Email { get; set; }
    
    // Esta propriedade não será preenchida pelo mapper
    [IgnoreMap] 
    public string InternalCode { get; set; }
}
```

**2. Atributo `[MapTo]` (Na Origem)**
Use quando você quer indicar na classe de domínio para qual propriedade ela deve ser mapeada no destino.

**Arquivo: `Domain/Entities/Product.cs`**
```csharp
using Prime.Architecture.Mapper.Attributes;

public class Product
{
    public int Id { get; set; }
    
    // Quando mapeado para um DTO que tenha a propriedade "ProductDescription",
    // o valor desta propriedade será usado automaticamente.
    [MapTo("ProductDescription")]
    public string Description { get; set; }
}
```

**3. Exemplo de Uso (Atributos)**

**Arquivo: `API/Controllers/ProductsController.cs`**
```csharp
[HttpGet("{id}")]
public async Task<IActionResult> GetSimple(int id)
{
    // Supondo que 'user' venha do repositório
    var user = await _userRepository.GetByIdAsync(id);
    
    // O mapeamento funciona automaticamente lendo os atributos [MapFrom]
    var simpleDto = _mapper.Map<User, SimpleUserDto>(user);
    
    return Ok(simpleDto);
}
```

---
*Desenvolvido com foco em escalabilidade e baixo consumo de recursos.*
