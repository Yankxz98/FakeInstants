# Implementação Completa: Sistema de Arquivos Orientado a Categorias

## Resumo das Mudanças

Sistema agora usa pastas com nomes de categorias em vez de GUIDs, com sincronização bidirecional entre disco e metadados.

---

## 1. Backend: Server/Program.cs

### Modificações no endpoint `/upload`:
- **Validação de categoryId**: Rejeita GUIDs e categoryId vazio
- **Estrutura de pastas**: `media/{categoryId}/{nome-arquivo-id.ext}`
- **Exemplo**: `media/memes/tiro-2dd09f17926445f18ab6978b3d90b9ff.mp3`

### Novo endpoint `/media/scan`:
- Varre recursivamente todas as pastas em `Server/media/`
- Extrai ID do arquivo (se existir no padrão `nome-{id}.ext`)
- Gera novo ID se padrão não for encontrado
- Atualiza `.media-index.json` automaticamente
- Retorna lista de `SoundDto` encontrados

### Novo endpoint `/media/migrate`:
- Reorganiza arquivos de pastas GUID para pastas de categorias
- Move arquivos de `media/{GUID}/arquivo.mp3` para `media/uncategorized/arquivo.mp3`
- Limpa pastas GUID vazias
- Retorna log detalhado da migração

### Classe `MediaIndexStore`:
- **Novo método**: `GetAllEntries()` para suporte à migração
- Mantém índice `id → caminho relativo` em `.media-index.json`

---

## 2. Frontend: Services/SoundManager.cs

### Método `AddSoundAsync`:
- Marcado como deprecated
- `FilePath` agora definido como vazio (será preenchido pelo backend)

### Novo método `SyncWithMediaDirectoryAsync`:
- Chama `/media/scan` no backend
- Adiciona novos sons encontrados no disco
- Atualiza `FilePath` de sons existentes
- Remove sons que não existem mais no disco
- Atualiza contadores de categoria automaticamente
- Persiste mudanças em `sounds.json`

---

## 3. Frontend: Components/Pages/Home.razor

### Botão de sincronização:
- Interface visual com spinner de loading
- Chamado automaticamente na inicialização
- Pode ser chamado manualmente pelo usuário
- Recarrega lista de categorias e sons após sincronização

---

## 4. Frontend: Components/Pages/MediaMigration.razor

### Nova página de migração:
- Interface para executar migração de arquivos
- Exibe log colorido da migração:
  - Verde: Arquivos migrados com sucesso
  - Vermelho: Erros
  - Amarelo: Arquivos pulados
  - Azul: Limpeza de diretórios
- Acessível via menu principal

---

## 5. Estrutura Final

```
Server/media/
├── .media-index.json           ← Índice id → caminho
├── memes/
│   ├── tiro-{id}.mp3
│   └── ...
├── efeitos/
│   ├── flash-{id}.mp3
│   └── ...
├── musica/
│   ├── thriller-{id}.mp3
│   └── ...
└── uncategorized/              ← Arquivos sem categoria
    └── ...
```

---

## 6. Fluxo de Dados

### Upload de arquivo:
1. Frontend → `POST /upload` com `categoryId` legível
2. Backend valida `categoryId` (não GUID, não vazio)
3. Backend salva em `media/{categoryId}/{nome-id.ext}`
4. Backend atualiza `.media-index.json`
5. Backend retorna `Sound` com `FilePath = "/media/{id}"`
6. Frontend persiste em `sounds.json`

### Sincronização (Scan):
1. Frontend → `GET /media/scan`
2. Backend varre `Server/media/*/*.{mp3,wav,ogg,etc}`
3. Backend extrai ID de cada arquivo
4. Backend retorna lista de `SoundDto`
5. Frontend mescla com `sounds.json`:
   - Adiciona novos sons
   - Atualiza `FilePath` de existentes
   - Remove sons que não existem no disco

### Categoria "Geral":
- `CategoryId = null` na interface
- Lista todos os sons independente da categoria
- Implementado através do filtro no `SoundList.razor` (linha 205-208)

---

## 7. Migração de Dados Existentes

### Passo a passo:
1. Acessar `/media-migration` no navegador
2. Clicar em "Iniciar Migração"
3. Sistema move arquivos de pastas GUID para `uncategorized/`
4. Atualiza `.media-index.json`
5. Remove pastas GUID vazias
6. Exibe log detalhado

### Ajuste manual (se necessário):
- Arquivos migrados para `uncategorized/` podem ser movidos manualmente para pastas de categorias
- Executar sincronização para atualizar o sistema

---

## 8. Categorias Padrão

### Removidas de `wwwroot/data/sounds.json`:
- Array `categories` agora vazio por padrão
- Categorias devem ser criadas via CRUD na interface

### Criação de categorias:
- Usar interface em `/categories`
- `Category.Id` deve ser string legível (ex: "memes", "efeitos")
- Nunca usar GUIDs como IDs de categoria

---

## 9. Validações Implementadas

### Backend (`/upload`):
- `categoryId` não pode ser vazio → 400 Bad Request
- `categoryId` não pode ser GUID → 400 Bad Request
- Extensão do arquivo deve estar na allowlist → 400 Bad Request
- Tamanho máximo: 100 MB (configurado em `FormOptions`)

### Frontend:
- Validação de tipos de arquivo no upload
- Sincronização automática na inicialização
- Feedback visual durante operações assíncronas

---

## 10. Endpoints Disponíveis

| Método | Endpoint | Descrição |
|--------|----------|-----------|
| GET | `/media/{id}` | Stream de áudio com Range/ETag |
| POST | `/upload` | Upload multipart com validação |
| GET | `/media/scan` | Varrer disco e retornar lista |
| POST | `/media/migrate` | Migrar arquivos GUID para categorias |

---

## 11. Variáveis de Ambiente

| Variável | Descrição | Padrão |
|----------|-----------|--------|
| `MEDIA_ROOT` | Diretório raiz para mídia | `media/` (relativo ao ContentRoot) |

Para deploy no Render:
- Criar Disk persistente
- Montar em `/app/media` (ou outro caminho)
- Configurar `MEDIA_ROOT=/app/media`

---

## 12. Próximos Passos (Opcional)

### Melhorias futuras:
1. **Auto-categorização**: Inferir categoria pelo nome do arquivo
2. **Bulk operations**: Mover múltiplos arquivos entre categorias
3. **Metadata extraction**: Ler tags ID3 de MP3 para preencher metadados
4. **Thumbnails**: Gerar waveform preview dos áudios
5. **Search indexing**: Índice full-text para busca rápida
6. **Category management**: Renomear categoria deve mover pasta no disco

---

## Checklist de Validação

- [x] Upload com `categoryId` legível cria arquivo em pasta correta
- [x] Upload com `categoryId` vazio retorna erro 400
- [x] Upload com GUID retorna erro 400
- [x] `/media/scan` retorna lista de arquivos existentes
- [x] `/media/migrate` reorganiza arquivos GUID
- [x] Sincronização adiciona novos arquivos do disco
- [x] Sincronização remove arquivos deletados do disco
- [x] Categoria "Geral" mostra todos os sons
- [x] Botão de sincronização funciona na interface
- [x] Página de migração acessível via menu
- [x] Categorias padrão removidas de `sounds.json`

