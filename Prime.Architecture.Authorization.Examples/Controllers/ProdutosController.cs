using Microsoft.AspNetCore.Mvc;
using Prime.Architecture.Authorization.Attributes;

namespace Prime.Architecture.Authorization.Examples.Controllers;

[ApiController, Route("api/produtos")]
public class ProdutosController : ControllerBase
{
    [HttpGet, RequerPermissao("produto", "read")]
    public IActionResult Listar() => Ok(new[] { "caneta", "caderno" });

    [HttpPost, RequerPermissao("produto", "create")]
    public IActionResult Criar() => Ok("criado");

    [HttpDelete("{id}"), RequerPermissao("produto", "delete")]
    public IActionResult Excluir(int id) => Ok($"excluído {id}");
}
