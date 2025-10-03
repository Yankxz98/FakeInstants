## Implementação no Render: streaming de áudio sem base64 (monólito simples)

### Objetivo
Eliminar o uso de `data:`/base64 para áudio, usando endpoints de streaming com suporte a Range/ETag a partir de disco, upload multipart e persistência apenas de metadados. Preparar deploy no Render como serviço web com disco persistente.

### Visão geral da arquitetura
- Frontend: Blazor (pode continuar WASM).
- Backend: ASP.NET Core (Hosted/Minimal API) com dois endpoints:
  - `GET /media/{id}`: streaming do arquivo com `enableRangeProcessing`, `ETag`, `Last-Modified`.
  - `POST /upload`: upload multipart para um diretório de mídia.
- Armazenamento de mídia: diretório configurado por variável de ambiente `MEDIA_ROOT` (montado em um Disk do Render).
- Persistência: apenas metadados (sem conteúdo de áudio) em JSON/arquivo/DB. O servidor mantém índice `id → caminho relativo`.

---

### 1) Ajustes no cliente (Blazor) — remover base64

1. Remover no `SoundUpload` a leitura completa do arquivo e a criação de `data:{mime};base64,...` para `FilePath`.
2. Substituir por upload multipart para `POST /upload` e usar a resposta do servidor (um `Sound` com `FilePath = "/media/{id}"`).
3. Em `AudioService`, para obter duração, use metadata do `<audio>` via `getAudioDuration(filePath)` em vez de baixar bytes com `HttpClient.GetByteArrayAsync`.

Snippets propostos (exemplificativos):

Upload multipart no cliente:

```csharp
using System.Net.Http.Headers;

private async Task<Sound?> UploadFileAsync(IBrowserFile file, string? categoryId)
{
    using var content = new MultipartFormDataContent();
    var stream = file.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
    var fileContent = new StreamContent(stream);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
    content.Add(fileContent, "file", file.Name);
    if (!string.IsNullOrEmpty(categoryId))
        content.Add(new StringContent(categoryId), "categoryId");

    var response = await Http.PostAsync("/upload", content);
    if (!response.IsSuccessStatusCode) return null;
    return await response.Content.ReadFromJsonAsync<Sound>();
}
```

Calcular duração sem baixar o arquivo:

```csharp
if (sound.Duration <= 0)
{
    try { sound.Duration = await GetAudioDurationAsync(sound.FilePath); }
    catch { sound.Duration = 0; }
}
```

`Sound.FilePath` deve ser sempre uma URL de streaming (ex.: `"/media/{id}"`). Não usar `data:` nem apontar diretamente para `wwwroot`.

---

### 2) Backend ASP.NET Core — endpoints mínimos

Observação: se o projeto atual é apenas Blazor WASM, adicione um projeto Server (Hosted) e hospede o WASM nele. Abaixo, exemplos de Minimal API para o projeto Server.

`GET /media/{id}` — streaming com Range/ETag/Last-Modified:

```csharp
app.MapGet("/media/{id}", async (string id, HttpContext ctx) =>
{
    var mediaRoot = Environment.GetEnvironmentVariable("MEDIA_ROOT");
    if (string.IsNullOrWhiteSpace(mediaRoot)) return Results.Problem("MEDIA_ROOT not configured");

    var relativePath = ResolveRelativePathFromId(id); // implementar resolução segura via índice
    var filePath = Path.Combine(mediaRoot, relativePath);
    if (!System.IO.File.Exists(filePath)) return Results.NotFound();

    var fileInfo = new FileInfo(filePath);
    var eTag = $"\"{fileInfo.Length}-{fileInfo.LastWriteTimeUtc.Ticks}\"";
    ctx.Response.Headers.ETag = eTag;
    ctx.Response.Headers.AcceptRanges = "bytes";
    ctx.Response.Headers.LastModified = fileInfo.LastWriteTimeUtc.ToString("R");

    var contentType = GetMimeType(fileInfo.Extension);
    return Results.File(filePath, contentType, enableRangeProcessing: true);
});
```

`POST /upload` — salvar arquivo e responder metadados do `Sound`:

```csharp
app.MapPost("/upload", async (HttpRequest req) =>
{
    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("file");
    var categoryId = form["categoryId"].FirstOrDefault();

    if (file is null || file.Length == 0) return Results.BadRequest();

    var mediaRoot = Environment.GetEnvironmentVariable("MEDIA_ROOT");
    if (string.IsNullOrWhiteSpace(mediaRoot)) return Results.Problem("MEDIA_ROOT not configured");

    var id = Guid.NewGuid().ToString("n");
    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    var safeName = Path.GetFileNameWithoutExtension(file.FileName);

    var relativePath = Path.Combine(categoryId ?? "uncategorized", $"{safeName}-{id}{ext}");
    var physicalPath = Path.Combine(mediaRoot, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

    using (var fs = File.Create(physicalPath))
    {
        await file.CopyToAsync(fs);
    }

    var fi = new FileInfo(physicalPath);
    var sound = new Sound
    {
        Id = id,
        Name = safeName,
        Description = string.Empty,
        CategoryId = categoryId ?? string.Empty,
        FileName = Path.GetFileName(physicalPath),
        FilePath = $"/media/{id}",
        FileSize = fi.Length,
        Duration = 0, // calcular no cliente via metadata
        Format = ext.TrimStart('.'),
        CreatedAt = DateTime.UtcNow
    };

    // Atualizar índice id → caminho relativo (persistir em storage do servidor)
    SaveIndex(id, relativePath); // implementar

    return Results.Ok(sound);
});
```

Funções auxiliares (a serem implementadas no Server):

```csharp
static string GetMimeType(string ext) => ext.ToLowerInvariant() switch
{
    ".mp3" => "audio/mpeg",
    ".wav" => "audio/wav",
    ".ogg" => "audio/ogg",
    ".aac" => "audio/aac",
    ".m4a" => "audio/mp4",
    _ => "application/octet-stream"
};

static string ResolveRelativePathFromId(string id)
{
    // Ler do índice seguro (ex.: JSON/DB) e retornar caminho relativo
    // Nunca aceitar input direto como caminho
    throw new NotImplementedException();
}

static void SaveIndex(string id, string relativePath)
{
    // Persistir mapeamento id → caminho relativo
    throw new NotImplementedException();
}
```

Recomendações de segurança:
- Mantener allowlist de diretórios de mídia.
- Validar extensão/MIME em upload.
- Nunca expor caminhos absolutos.
- Implementar `HEAD` em `/media/{id}` se possível.

---

### 3) Persistência de metadados
- Persistir somente metadados (`Sound`, `Category`, etc.).
- O índice `id → caminho relativo` deve residir no servidor e ser a fonte de verdade para o `ResolveRelativePathFromId`.
- O cliente consome `Sound.FilePath` como URL `/media/{id}` e não precisa conhecer caminhos físicos.

---

### 4) Deploy no Render

Configuração do serviço Web:
- Porta: garantir `ASPNETCORE_URLS=http://0.0.0.0:8080`.
- Build Command: `dotnet publish -c Release -o out`.
- Start Command: `dotnet out/SeuProjeto.Server.dll` (ajuste para o nome correto do assembly Server).

Disco persistente:
- Adicionar um Disk ao serviço.
- Montar o Disk em um caminho do container e configurar `MEDIA_ROOT` apontando para esse caminho.
- Garantir permissões de escrita/leitura no diretório.

Variáveis de ambiente:
- `MEDIA_ROOT`: caminho do diretório onde os arquivos de áudio serão gravados e lidos.
- Opcional: qualquer configuração de log/diagnóstico conforme necessário.

Rede/headers:
- Habilitar `Accept-Ranges` (feito no endpoint).
- Retornar `ETag` e `Last-Modified` para cache eficiente.

Observações de plano gratuito:
- Cold start pode ocorrer; mantenha o monólito enxuto.
- Discos podem implicar custo; verifique limitações do plano.

---

### 5) Checklist de validação
- [ ] Upload via `/upload` retorna `200` e um `Sound` com `FilePath = "/media/{id}"`.
- [ ] Reproduzir `<audio src="/media/{id}">` com seek funcionando (status `206 Partial Content`).
- [ ] `HEAD /media/{id}` (se implementado) retorna `200` com `Content-Length`, `ETag`, `Last-Modified`.
- [ ] Reiniciar o serviço: arquivos enviados permanecem (Disk persistente).
- [ ] JSON de metadados não contém payloads base64 de áudio.

---

### 6) Impacto no código existente
- `Components/SoundUpload.razor`: remover criação de `data:`/base64; enviar multipart e usar resposta do servidor.
- `Services/AudioService.cs`: não baixar bytes do áudio; obter duração via metadata do elemento `<audio>`.
- `Services/SoundManager.cs`: não construir `FilePath` apontando `wwwroot`; usar o `FilePath` fornecido pelo backend.
- `Services/JsonStorageService.cs`: manter apenas metadados; não incluir conteúdo de áudio.

Seguindo estas instruções, o projeto passa a servir áudio direto do disco com streaming adequado, evitando estouro de tamanho/armazenamento causado por base64 e ficando pronto para hospedar no Render.



