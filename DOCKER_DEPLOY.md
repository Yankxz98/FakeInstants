# Deploy Docker no Render

Este documento explica como fazer o deploy da aplicação FakeInstants no Render usando Docker.

## Arquivos Docker criados

- `Dockerfile`: Configuração para construir a imagem do container
- `docker-compose.yml`: Configuração opcional para desenvolvimento local
- `.dockerignore`: Arquivos a serem ignorados durante o build

## Configuração no Render

### 1. Criar um novo serviço Web

1. Acesse o [Render Dashboard](https://dashboard.render.com)
2. Clique em "New" → "Web Service"
3. Conecte seu repositório Git

### 2. Configurações do serviço

**Build Settings:**
- **Build Command**: (deixe em branco - Render fará automaticamente)
- **Dockerfile Path**: `./Dockerfile` (padrão)

**Runtime:**
- **Container Port**: `8080`

**Environment Variables:**
- `MEDIA_ROOT`: `/app/media` (ou caminho onde o disco persistente será montado)

### 3. Disco persistente (opcional mas recomendado)

Para persistir os arquivos de áudio entre deploys:

1. No Render Dashboard, vá para "Disks"
2. Crie um novo disco com:
   - **Name**: `fakeinstants-media`
   - **Size**: Pelo menos 1GB (ajuste conforme necessário)
   - **Mount Path**: `/app/media`

3. No serviço web, em "Advanced" → "Disks":
   - Selecione o disco criado
   - **Mount Path**: `/app/media`

## Desenvolvimento local

Para testar localmente antes do deploy:

```bash
# Construir e executar com docker-compose
docker-compose up --build

# Ou diretamente com Docker
docker build -t fakeinstants .
docker run -p 8080:8080 -v ./media:/app/media fakeinstants
```

A aplicação estará disponível em `http://localhost:8080`

## Troubleshooting

### Problemas de build no Docker

Se encontrar erros de compilação relacionados a projetos referenciados:

1. **Erro "Program does not contain a static 'Main' method"**: O projeto Blazor está sendo compilado incorretamente. Verifique se o `Program.cs` está sendo copiado no Dockerfile.

2. **Erro "The referenced project does not exist"**: Caminhos de referência estão incorretos. No Docker, todos os arquivos estão no mesmo diretório.

3. **Conflito de Program.cs**: O Server.csproj tem exclusão para `Program.cs` para evitar conflitos com o projeto Blazor.

### Estrutura de arquivos no Docker

O Dockerfile copia:
- `Server.csproj` e pasta `Server/` (projeto ASP.NET Core)
- `fakeinstants.csproj`, `Components/`, `Models/`, `Services/`, `wwwroot/`, `Program.cs` (projeto Blazor referenciado)

O `Server.csproj` referencia o projeto Blazor mas exclui o `Program.cs` da raiz para evitar conflitos.

### Configuração do projeto Blazor

O projeto `fakeinstants.csproj` tem `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>` necessário para compilação correta no Docker.

## Health Check

A imagem inclui um health check que verifica se a aplicação está respondendo corretamente. O Render usará isso para monitorar o status do serviço.

## Segurança

- A imagem usa um usuário não-root (`appuser`) para maior segurança
- Variáveis de ambiente são configuradas para produção
- O container escuta em `0.0.0.0:8080` conforme esperado pelo Render

## Troubleshooting

### Porta não exposta
Certifique-se de que o serviço no Render está configurado para usar a porta 8080.

### Disco não montado
Se os arquivos não persistirem entre deploys, verifique se o disco persistente está corretamente montado em `/app/media`.

### Erro de build
Verifique os logs do build no Render. Possíveis causas:
- Arquivo `Server.csproj` não encontrado na raiz
- Dependências não restauradas corretamente
- Arquivos grandes demais no contexto do build

### MEDIA_ROOT não configurado
Certifique-se de que a variável de ambiente `MEDIA_ROOT` está definida como `/app/media` (ou o caminho onde o disco está montado).
