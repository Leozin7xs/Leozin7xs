# WP Support Tools

Ferramenta Windows de primeiro atendimento para as unidades do Grupo WP.
O funcionário abre o aplicativo, clica em **Corrigir problema de impressão**, e o aplicativo
executa uma sequência fixa de procedimentos de manutenção, mostrando o progresso passo a passo.

> **Status atual (v0.2.0):** procedimento de impressão da WP implementado (fila + spooler + TSPrint).
> Pendente: caminho do `TSPrint.exe` nas unidades. Até lá, a etapa "Abrindo o TSPrint" é só simulada.

---

## Tecnologia

| Item | Escolha |
|---|---|
| Linguagem / plataforma | C# com **.NET 10 (LTS)** |
| Interface | **WPF** (nativa do Windows) |
| Ações no Windows | APIs do .NET/Windows (ex.: `ServiceController`, `System.IO`) e, quando necessário, PowerShell com script fixo embutido |
| Elevação | UAC (mecanismo oficial do Windows), sob demanda |
| Distribuição | Um único `WPSupportTools.exe`, sem dependências |

## Estrutura

```
WPSupportTools.sln
├── src/
│   ├── WPSupportTools.Core/            Lógica (sem interface)
│   │   ├── Procedures/                 Contrato de procedimento, sequência
│   │   ├── Execution/                  Executor determinístico, eventos, protocolo do modo elevado
│   │   ├── Logging/                    Log local em arquivo
│   │   └── Modules/
│   │       ├── ModuleCatalog.cs        Módulos exibidos na tela inicial
│   │       ├── SequenceCatalog.cs      Lista FECHADA de sequências executáveis
│   │       └── Printing/               Impressão
│   │           ├── Correction/
│   │           │   ├── PrintingFixSequence.cs    ← ordem das etapas
│   │           │   └── Procedures/               ← um arquivo por procedimento
│   │           └── Diagnostics/                  (futuro)
│   └── WPSupportTools.App/             Interface WPF + modo elevado
│       ├── Elevation/                  UAC, canal local com o processo administrador
│       ├── ViewModels/
│       └── MainWindow.xaml
└── tests/WPSupportTools.Core.Tests/    Testes automatizados
```

Módulos futuros (UniLab, Rede, Telefonia) entram como novas pastas em `Modules/`, registradas em
`ModuleCatalog` e `SequenceCatalog`. Nada mais precisa mudar.

## Sequência de impressão (atual)

Procedimento manual da WP, definido em
`src/WPSupportTools.Core/Modules/Printing/Correction/PrintingFixSequence.cs`:

```
1. Ver se a fila de impressão está travada.
   Se SIM → parar o spooler, limpar a fila, iniciar o spooler   (net stop / net start spooler)
   Se NÃO → ver se o TSPrint está aberto; se não estiver, abrir.
```

| # | Etapa | ID (log) | Quando roda | Admin |
|---|---|---|---|---|
| 1 | Verificando fila de impressão | `printing.check-queue` | sempre | não |
| 2 | Parando serviço de impressão | `printing.stop-spooler` | fila travada | sim |
| 3 | Limpando fila de impressão | `printing.clear-queue` | fila travada | sim |
| 4 | Iniciando serviço de impressão | `printing.start-spooler` | fila travada | sim |
| 5 | Verificando o TSPrint | `printing.check-tsprint` | fila normal | não |
| 6 | Abrindo o TSPrint | `printing.start-tsprint` | fila normal e TSPrint fechado | não |

Etapas que não se aplicam aparecem como "não necessária". O aviso do UAC só aparece quando a fila
está travada, uma única vez para as etapas 2–4. O TSPrint é aberto como o próprio usuário, nunca como administrador.

**Fila travada** = há ao menos um documento com erro, offline, sem papel, bloqueado ou pedindo intervenção,
ou um documento parado na fila há mais de 2 minutos. Spooler parado também conta como travado.
Os valores ficam em `PrintingConstants.cs`.

### Regras de segurança por etapa

- **Parar spooler:** só o serviço `Spooler`. Se outro serviço depender dele e estiver rodando, não para nada.
- **Limpar fila:** só com o spooler parado; só na pasta padrão `%SystemRoot%\System32\spool\PRINTERS`, e só se for
  a pasta configurada no Windows; só arquivos `.SPL` e `.SHD`; não entra em subpastas nem segue links.
- **Iniciar spooler:** se o serviço estiver desativado no Windows, não altera; informa o suporte.
- **TSPrint:** só verifica o processo `TSPrint` da sessão do usuário; só abre o executável do caminho fixo
  configurado em `PrintingConstants.TSPrintExecutablePath`. Configurações do TSPrint (inclusive senha) não são lidas.

### Regras de execução

- As etapas rodam **na ordem**, uma de cada vez.
- **Na primeira falha a sequência para.** As etapas seguintes são marcadas como "não executadas".
- Não há novas tentativas nem ações alternativas automáticas.
- Cada etapa tem tempo limite; estourar o tempo conta como falha.
- A janela não pode ser fechada no meio da manutenção (para não deixar o spooler parado).

## Elevação de privilégios (UAC)

1. A interface abre **sem** privilégios de administrador (`asInvoker` em `app.manifest`).
2. Ao chegar a etapas administrativas, o app inicia **o próprio executável**
   com o verbo `runas`. O Windows mostra o aviso do UAC.
3. O processo elevado recebe somente `--worker --sequence printing.fix --steps 1,2,3 --pipe <nome> --parent <pid>`.
   Ele só aceita sequências do `SequenceCatalog` e só etapas que exigem administrador:
   **não existe forma de passar um comando**.
4. A comunicação é por *named pipe* local com nome aleatório. Os dois lados conferem o PID um do outro
   (`GetNamedPipeClientProcessId` / `GetNamedPipeServerProcessId`).
5. Se o usuário recusar o UAC, nada é executado e a tela informa que a permissão é necessária.

Recomendações de implantação:
- Instalar em `C:\Program Files\WP Support Tools\` (pasta que usuários comuns não podem alterar).
- **Assinar o executável** com o certificado de assinatura de código da WP, para o UAC mostrar
  "Editor verificado: Grupo WP" e o SmartScreen não bloquear.

## Log

- Local: `%LOCALAPPDATA%\WPSupportTools\Logs\wpst-AAAAMMDD.log` (um arquivo por dia).
- Registra: data/hora, versão, cada etapa (ID, nome, resultado), mensagem de erro, retorno do usuário.
- Não registra nome de usuário, nome do computador, nomes de documentos nem dados pessoais.
- Botão "Abrir pasta de logs" no rodapé.

Exemplo:

```
[2026-10-20 10:41:03] [INFO ] Início: printing.fix — Corrigindo problema de impressão… (WP Support Tools v0.2.0; ...)
[2026-10-20 10:41:03] [INFO ] Fila de impressão: 2 trabalho(s) na fila (Samsung M408x Series: 2); com erro: 1; há mais de 2 min: 2.
[2026-10-20 10:41:03] [INFO ] Etapa 1/6 [printing.check-queue] Verificando fila de impressão: OK — Fila travada (2 documento(s) parado(s)).
[2026-10-20 10:41:09] [INFO ] Etapa 2/6 [printing.stop-spooler] Parando serviço de impressão: OK — Serviço de impressão parado.
[2026-10-20 10:41:09] [INFO ] Etapa 3/6 [printing.clear-queue] Limpando fila de impressão: OK — Fila limpa (4 arquivo(s) removido(s)).
[2026-10-20 10:41:11] [INFO ] Etapa 4/6 [printing.start-spooler] Iniciando serviço de impressão: OK — Serviço de impressão iniciado.
[2026-10-20 10:41:11] [INFO ] Etapa 5/6 [printing.check-tsprint] Verificando o TSPrint: NÃO NECESSÁRIO — Não necessário: a fila foi reiniciada.
```

## Internet

O aplicativo **não faz nenhuma conexão de rede**: sem telemetria, sem verificação de atualização,
sem envio de logs. Tudo roda localmente.

## Compilar, testar e gerar o .exe

Pré-requisito (somente na máquina de desenvolvimento): [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet test                                  # testes automatizados
dotnet run --project src\WPSupportTools.App  # abrir o app (Windows)
powershell -File build\publish.ps1           # gera publish\win-x64\WPSupportTools.exe
```

O `.exe` gerado é *self-contained* (≈60 MB) e roda em Windows 10/11 x64 sem instalar o .NET.

## Como um procedimento real será implementado

Cada etapa é uma classe que implementa `IProcedure` (ver `Modules/Printing/Correction/Procedures/`):

```csharp
public sealed class StopPrintSpoolerProcedure : IProcedure
{
    public string Id => "printing.stop-spooler";
    public string DisplayName => "Parando serviço de impressão";
    public bool RequiresElevation => true;

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken ct)
    {
        // 1. Validar que o alvo existe e é o esperado (se não for: retornar falha, sem alternativas).
        // 2. Executar somente o comando autorizado.
        // 3. Confirmar o resultado e retornar Ok/Fail com mensagem clara.
    }
}
```

O que o programa **nunca** faz: instalar/remover programas, alterar drivers, rede ou registro sem
autorização explícita, apagar arquivos fora das pastas autorizadas, executar comandos arbitrários,
tentar soluções extras automaticamente, acessar outros computadores ou enviar dados para fora.
