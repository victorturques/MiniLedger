# MiniLedger

[![CI](https://github.com/victorturques/MiniLedger/actions/workflows/ci.yml/badge.svg)](https://github.com/victorturques/MiniLedger/actions/workflows/ci.yml)

Mini banco digital de portfólio: API REST em ASP.NET Core com frontend integrado, onde o cliente abre conta, deposita, transfere entre contas e consulta o extrato.

O objetivo do projeto é praticar o que raramente aparece em CRUD de tutorial: **consistência de saldo sob concorrência**, **idempotência em operações financeiras** e **autorização por dono do recurso**.

**Demo ao vivo:** _em breve_

## Testando em 30 segundos

Não existe conta de demonstração — cada visitante cria a própria, e os dados ficam salvos no servidor.

1. Abra a demo e clique em **Criar conta**. Anote o número de 8 dígitos que aparece no cartão.
2. Saia e entre com esse número + a senha que você definiu.
3. Deposite R$ 100,00.
4. Em uma janela anônima, crie uma segunda conta e transfira R$ 25,00 para ela.
5. Volte para a primeira conta e confira o extrato: um depósito e uma transferência enviada.

## O que você vê

| Tela | Conteúdo |
|---|---|
| Home (deslogado) | Apresentação com os botões **Criar conta** e **Entrar na conta**, que abrem modais |
| Home (logado) | Cartão digital com nome do titular, número da conta (com botão **Copiar**) e saldo atualizado |
| Adicionar saldo | Campo de valor com máscara de moeda e o resultado da operação logo abaixo |
| Transferências | Conta de destino (8 dígitos) + valor, com feedback de sucesso ou erro |
| Extrato | Lista paginada com tipo (débito/crédito), contraparte, valor e data |

O frontend é uma SPA leve em HTML/CSS/JS puro, servida pela própria API a partir de `wwwroot` — não há build de frontend nem projeto separado.

## Funcionalidades

| Área | O que está implementado |
|---|---|
| Conta | Cadastro com nome, senha (hash BCrypt) e saldo inicial; número de conta de 8 dígitos gerado pelo servidor |
| Login | Número da conta + senha, retornando JWT |
| Depósito | Crédito com validação de valor e atualização de saldo |
| Transferência | Débito e crédito na mesma transação, com controle de concorrência otimista |
| Extrato | Lista paginada de transações, com contraparte nas transferências |
| Idempotência | `Idempotency-Key` em depósito e transferência: repetir a chave não duplica a operação |
| Segurança | Autorização por dono da conta (403 para conta alheia), rate limit no login, validação server-side |
| Observabilidade | Logs estruturados com Serilog, health check em `/health`, erros no padrão RFC 7807 |

## Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 10 / C# |
| API | ASP.NET Core Web API (controllers) |
| Persistência | Entity Framework Core 10 + SQLite |
| Autenticação | JWT Bearer |
| Validação | FluentValidation |
| Rate limit | AspNetCoreRateLimit (por IP) |
| Logs | Serilog (console + arquivo rotativo diário) |
| Frontend | HTML, CSS e JavaScript puro, servido de `wwwroot` |
| Testes | xUnit + `WebApplicationFactory` (170 testes) |

## Arquitetura

| Projeto | Responsabilidade |
|---|---|
| `MiniLedger.Domain` | Entidades (`Account`, `Transaction`) e enums; sem dependência de infraestrutura |
| `MiniLedger.Application` | DTOs, validators, serviços de conta/transferência, idempotência e contratos |
| `MiniLedger.Infrastructure` | `AppDbContext`, mapeamentos e migrations do EF Core |
| `MiniLedger.Api` | Controllers, middleware de exceção, configuração, JWT e frontend estático |
| `MiniLedger.Tests` | Testes unitários e ponta a ponta |

Fluxo de uma requisição: `Controller` → validação (FluentValidation) → serviço de aplicação → `AppDbContext` → SQLite. Erros sobem padronizados pelo middleware, e as operações de escrita passam pelo store de idempotência.

### Modelo de dados

| Entidade | Campos principais | Observação |
|---|---|---|
| `Account` | `Id`, `HolderName`, `AccountNumber`, `PasswordHash`, `Balance`, `CreatedAt`, `Version` | `AccountNumber` tem índice único; `Balance` é materializado e atualizado na mesma operação |
| `Transaction` | `Id`, `AccountId`, `TransferId`, `Type` (Debit/Credit), `Amount`, `CounterpartyAccountNumber`, `CreatedAt` | Uma transferência gera **duas** transações com o mesmo `TransferId` |

**Concorrência:** `Account.Version` é um token de concorrência otimista, incrementado a cada `SaveChanges`. Se duas operações alterarem a mesma conta ao mesmo tempo, a segunda é rejeitada pelo banco, a transação é revertida e o cliente recebe 400 para tentar de novo — o saldo nunca fica inconsistente.

## Endpoints

| Método | Rota | Auth | Descrição | Respostas |
|---|---|---|---|---|
| `POST` | `/api/accounts` | Público | Cria conta | 201, 400 |
| `POST` | `/api/accounts/access` | Público | Login, devolve JWT | 200, 400, 401, 429 |
| `GET` | `/api/accounts/by-number/{accountNumber}` | JWT | Consulta a própria conta | 200, 403, 404 |
| `GET` | `/api/accounts/by-number/{accountNumber}/transactions?page=1&pageSize=20` | JWT | Extrato paginado (máx. 100 por página) | 200, 403, 404 |
| `POST` | `/api/accounts/deposit` | JWT | Depósito, aceita `Idempotency-Key` | 200, 400, 403, 404, 409 |
| `POST` | `/api/transfers` | JWT | Transferência, aceita `Idempotency-Key` | 200, 400, 403, 404, 409 |
| `GET` | `/health` | Público | Health check | 200 |
| `GET` | `/swagger` | Público | Documentação interativa | 200 |

Regras de idempotência: mesma chave + mesmo payload devolve a resposta original; mesma chave + payload diferente devolve **409 Conflict**.

## Usando a API

Com a API rodando em `http://localhost:5000`:

```bash
# 1) Criar conta — guarde o accountNumber devolvido
curl -X POST http://localhost:5000/api/accounts \
  -H "Content-Type: application/json" \
  -d '{"holderName":"Ana","password":"senha123","confirmPassword":"senha123","initialBalance":0}'

# 2) Login — guarde o token devolvido
curl -X POST http://localhost:5000/api/accounts/access \
  -H "Content-Type: application/json" \
  -d '{"accountNumber":"00000001","password":"senha123"}'

# 3) Depósito com chave de idempotência
curl -X POST http://localhost:5000/api/accounts/deposit \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: deposito-001" \
  -d '{"accountNumber":"00000001","amount":100.00}'

# 4) Transferência
curl -X POST http://localhost:5000/api/transfers \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: transferencia-001" \
  -d '{"fromAccountNumber":"00000001","toAccountNumber":"00000002","amount":25.00}'
```

Repetir o passo 3 com a mesma chave devolve 200 com a resposta original. Trocar o valor e repetir a chave devolve 409.

## Como rodar

Pré-requisito: .NET SDK 10.

```bash
dotnet run --project src/MiniLedger.Api
```

| Endereço | O que é |
|---|---|
| http://localhost:5000 | Frontend |
| http://localhost:5000/swagger | Swagger |
| http://localhost:5000/health | Health check |

Na primeira execução a API aplica as migrations e cria o `miniledger.db` automaticamente — não precisa rodar `dotnet ef database update`. Em Development, banco e logs ficam na pasta do projeto da API.

## Configuração

| Chave | Padrão (Development) | Produção |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `Data Source=miniledger.db` | `Data Source={PrivateFolder}/miniledger.db` |
| `Jwt:Key` | chave de desenvolvimento no `appsettings.json` | obrigatória via variável de ambiente `Jwt__Key` |
| `Jwt:ExpireMinutes` | 60 | 60 |
| `IpRateLimiting` | login: 5/min e 20/h por IP | igual |

O placeholder `{PrivateFolder}` é resolvido no startup para uma pasta irmã do conteúdo publicado, fora de `wwwroot`, para que banco e logs sobrevivam a um novo deploy. Em produção, a chave JWT nunca fica no repositório:

```bash
export Jwt__Key="sua-chave-forte-aqui"        # Linux
$env:Jwt__Key = "sua-chave-forte-aqui"        # PowerShell
```

## Testes

```bash
dotnet test
```

A suíte cobre validators, serviços, controllers, `TokenService`, middleware de exceção, idempotência, rate limit e o fluxo ponta a ponta (criar conta → login → depósito → transferência → extrato) usando `WebApplicationFactory`. Os testes de integração sobem a API real com um SQLite temporário e as migrations aplicadas, sem tocar no banco de desenvolvimento.

## Decisões que valem destacar

| Decisão | Motivo |
|---|---|
| Autorização por dono da conta | Um usuário autenticado não pode ler nem movimentar a conta de outro (403) |
| Idempotência com fingerprint do payload | Evita duplicidade em retry de rede e detecta reuso indevido da chave |
| Rate limit por IP | Protege o login contra força bruta sem depender de header enviado pelo cliente |
| Número de conta numérico de 8 dígitos | Fácil de digitar no login e no teste manual |
| Migrations no startup | O servidor sobe com o banco pronto, sem passo manual de deploy |
| Banco fora de `wwwroot` | Deploy não apaga os dados |

## Limitações conhecidas

Projeto de estudo, não um banco de verdade. Os pontos abaixo são conscientes:

| Limitação | Efeito | Como seria em produção |
|---|---|---|
| Idempotência em memória | Chaves somem ao reiniciar e não são compartilhadas entre instâncias | Redis ou tabela dedicada |
| Rate limit em memória | Mesmo comportamento, por instância | Store distribuído |
| SQLite em arquivo único | Escrita serializada; não escala horizontalmente | PostgreSQL ou SQL Server |
| Sem refresh token | Sessão expira em 60 minutos e exige novo login | Refresh token + revogação |
| Sem PIX, agendamento, cartão ou limite | Fora do escopo do mini-banco | — |
| Sem recuperação de senha e 2FA | Fora do escopo | — |

## Licença

MIT — veja [LICENSE](LICENSE).
