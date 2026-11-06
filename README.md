# WP Support Tools

Ferramenta Windows de primeiro atendimento para as unidades do Grupo WP.
O funcionário abre o aplicativo, clica em **Corrigir problema de impressão**, e o aplicativo
executa uma sequência fixa de procedimentos de manutenção, mostrando o progresso passo a passo.

> **Status atual (v0.6.3):** procedimento de impressão da WP implementado (fila + spooler + TSPrint).
> A etiquetadora passa a ser selecionada no TSPrint automaticamente (pasta `etiq`).

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
1. Ver se a fila de impressão está travada (documento com erro).
   Se SIM → cancelar os documentos da impressora travada, net stop / net start spooler, conferir se destravou.
   Se NÃO → ver se o TSPrint está aberto; se não estiver, abrir.
```

| # | Etapa | ID (log) | Quando roda | Admin |
|---|---|---|---|---|
| 1 | Verificando fila de impressão | `printing.check-queue` | sempre | não |
| 2 | Cancelando documentos travados | `printing.cancel-stuck-jobs` | fila travada | sim |
| 3 | Parando serviço de impressão | `printing.stop-spooler` | fila travada | sim |
| 4 | Limpando arquivos da fila | `printing.clear-spool-files` | fila travada | sim |
| 5 | Iniciando serviço de impressão | `printing.start-spooler` | fila travada (**sempre**, se o spooler foi parado) | sim |
| 6 | Cancelando documentos restantes | `printing.cancel-remaining-jobs` | fila travada | sim |
| 7 | Conferindo se a fila destravou | `printing.recheck-queue` | fila travada | não |
| 8 | Verificando o TSPrint | `printing.check-tsprint` | fila normal | não |
| 9 | Abrindo o TSPrint | `printing.start-tsprint` | fila normal e TSPrint fechado | não |

Etapas que não se aplicam aparecem como "não necessária". O aviso do UAC só aparece quando a fila
está travada, uma única vez para as etapas 2–6. O processo de administrador recebe a lista das impressoras
travadas e mexe somente nelas. O TSPrint é aberto como o próprio usuário, nunca como administrador.

**Fila travada** = há ao menos um documento com status de erro, offline, sem papel, bloqueado ou pedindo
intervenção. Documento só demorando (sem erro) não conta. Spooler parado ou fila que não responde
(consulta demora mais de 10 s) também contam como travada.

### Regras de segurança por etapa

- **Cancelar documentos:** igual a "Cancelar todos os documentos" na janela da fila, feito pelo próprio
  serviço de impressão, **somente nas impressoras com documento travado**. Se a fila estiver tão travada que
  não deixa cancelar, a etapa não interrompe: segue para parar o serviço e limpar os arquivos.
- **Limpar arquivos da fila** (com o spooler parado): apaga os .SHD/.SPL **só das impressoras travadas**
  (identificadas pelo nome da impressora dentro do .SHD); se a fila nem pôde ser lida, apaga todos, como o
  técnico faz à mão. Só a pasta padrão `%SystemRoot%\System32\spool\PRINTERS`, sem subpastas nem links.
- **Parar spooler:** só o serviço `Spooler`. Se outro serviço depender dele e estiver rodando, não para nada.
- **Iniciar spooler:** roda **mesmo se uma etapa anterior falhar** — o spooler nunca fica desligado ao final.
  Se o serviço estiver desativado no Windows, não altera; informa o suporte.
- **Cancelar restantes:** documentos presos em "excluindo" podem voltar quando o spooler reinicia; são cancelados de novo.
- **Conferir fila:** a fila das impressoras tratadas tem que ficar **vazia**; se sobrar qualquer documento,
  a correção falha e orienta chamar o técnico (spooler ligado).
- **TSPrint:** só verifica o processo `TSPrint` da sessão do usuário; só abre o executável do caminho fixo
  `C:\Rede_Unilab\TSPrint\TSPrint.exe` (`PrintingConstants.TSPrintExecutablePath`). Da configuração do TSPrint,
  só é usado o arquivo de impressoras `C:\Rede_Unilab\TSPrint\conf\print_doc_cnf.data`; o arquivo geral, com
  usuário e senha, nunca é lido. A única alteração possível é o nome da impressora da pasta de etiquetas
  (`etiq`/`etiqueta`), e só quando ela não aponta para a Zebra localizada: o TSPrint é fechado, o nome é trocado
  (o resto do arquivo fica idêntico; cópia do anterior em `%LOCALAPPDATA%\WPSupportTools\Backups`) e o TSPrint
  é reaberto.

### Regras de execução

- As etapas rodam **na ordem**, uma de cada vez.
- **Na primeira falha a sequência para.** As etapas seguintes são marcadas como "não executadas",
  exceto religar o spooler, que sempre roda se ele tiver sido parado.
- Não há novas tentativas nem ações alternativas automáticas.
- Cada etapa tem tempo limite; estourar o tempo conta como falha — inclusive quando o Windows trava numa
  chamada (ex.: spooler sem resposta): a etapa é abandonada e a janela volta a responder.
- A janela não pode ser fechada no meio da manutenção (para não deixar o spooler parado).

## Etiquetadora (botão "Problema na etiquetadora")

A funcionária escolhe **"Não sai nada"** ou **"Sai desconfigurada"**. Padrão WP em `docs/padrao-etiquetadoras.md`.

**Escolha da etiquetadora:** só drivers `ZDesigner`. Ignora cópias offline; se houver mais de uma ligada,
usa a que o **TSPrint** tem configurada para etiquetas (pasta `etiq` em `print_doc_cnf.data`); se não der,
a padrão do Windows; senão não adivinha e pede o técnico. Se todas estiverem offline, avisa
"desligada ou desconectada" e não faz mais nada.

| "Não sai nada" | "Sai desconfigurada" |
|---|---|
| 1. Localizando a etiquetadora | 1. Localizando a etiquetadora |
| 2. Fila **só da etiquetadora**: qualquer documento parado conta | 2. Conferindo tamanho (50,8 × 25,4 mm, retrato) |
| 3–8. Se houver: cancelar, parar spooler, limpar arquivos da Zebra, iniciar, cancelar restantes, conferir vazia | 3. Se fora do padrão: corrigir nas preferências do usuário |
| 9. TSPrint imprimindo etiquetas nessa Zebra? Se não, troca | 4. Calibrar (ZPL `~JC` / EPL `xa`) |
| 10–11. Se a fila estava normal: TSPrint aberto? | 5. TSPrint imprimindo etiquetas nessa Zebra? Se não, troca |
| 12. Etiqueta de teste | 6. Etiqueta de teste |

A etiqueta de teste é enviada direto à impressora (RAW), porque a página de teste do Windows sai em branco
no padrão WP. Se a etiqueta de teste sai e a do UniLab não, o problema está no TSPrint/UniLab.

**Configuração modelo (técnico):** com uma Zebra deixada exatamente no padrão, rode
`WPSupportTools.exe --capturar-modelo`. Não altera nada: grava em `Área de Trabalho\WPST-configuracao-modelo`
um `.wpst-modelo.json` por Zebra (preferências completas do driver, incluindo o bloco interno com escuridão,
modo, offsets e gráficos, mais driver e versão) e uma cópia de reserva do Windows (`printui /Ss`).
Passo a passo em `docs/padrao-etiquetadoras.md`.

**Ainda não feito:** conferir/corrigir escuridão, velocidade, modo, offsets e gráficos (depende da
"configuração modelo" — ver docs). Comandos de calibração a validar com cada modelo.

## Elevação de privilégios (serviço ou UAC)

Com o **serviço instalado**, a interface envia o pedido pelo canal local `WPSupportTools.Service` e o
serviço (conta de sistema) executa — sem senha. O serviço só aceita etapas de administrador de sequências
do catálogo, com as informações que a sequência permite (mesma validação descrita abaixo).
Sem o serviço (ou se ele não responder), vale o fluxo pelo UAC:

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
[2026-10-20 10:41:03] [INFO ] Fila de impressão: 2 documento(s) na fila (Samsung M408x Series: 2 (Error, Printing)); com erro: 1.
[2026-10-20 10:41:03] [INFO ] Etapa 1/7 [printing.check-queue] Verificando fila de impressão: OK — Fila travada (1 documento(s) com erro).
[2026-10-20 10:41:09] [INFO ] Etapa 2/7 [printing.cancel-stuck-jobs] Cancelando documentos travados: OK — Documentos cancelados em: Samsung M408x Series.
[2026-10-20 10:41:09] [INFO ] Etapa 3/7 [printing.stop-spooler] Parando serviço de impressão: OK — Serviço de impressão parado.
[2026-10-20 10:41:11] [INFO ] Etapa 4/7 [printing.start-spooler] Iniciando serviço de impressão: OK — Serviço de impressão iniciado.
[2026-10-20 10:41:11] [INFO ] Etapa 5/7 [printing.recheck-queue] Conferindo se a fila destravou: OK — Fila destravada.
```

## Unidade e painel de resultados

**Primeiro acesso:** o app pergunta o **laboratório** (Santa Helena, Widal ou Endocrimeta — lista fixa em
`UnitInfo.Laboratories`) e o **nome da unidade** (digitado). O nome é padronizado ("menino   DEUS" → "Menino Deus")
e guardado em `%ProgramData%\WPSupportTools\unidade.json` (vale para todos os usuários do PC; se não der para
gravar lá, fica em `%LOCALAPPDATA%`). Aparece no cabeçalho; "Alterar unidade" no rodapé.

**Registros de uso** (para o painel): a cada atendimento o app guarda até dois registros, com o mesmo `atendimento_id`:

| Campo | Exemplo | Observação |
|---|---|---|
| `tipo` | `execucao` / `retorno` | execução terminou / funcionária respondeu |
| `resultado` | `concluido`, `falhou`, `sem_permissao`, `simulado` | só em `execucao` |
| `etapa_com_falha` | `labels.locate` | só quando falhou |
| `resolvido` | `true` / `false` | só em `retorno` ("Funcionou" / "Não funcionou") |
| `laboratorio`, `unidade` | `Widal`, `Menino Deus` | preenchidos no primeiro acesso |
| `pc_id` | `3f2a…` (16 caracteres) | resumo do MachineGuid — conta PCs, não identifica o nome do PC |
| `sequencia`, `versao`, `ocorrido_em`, `id` | | |

**Não vai:** nome de usuário, nome do computador, nomes de documentos, mensagens de erro.

Os registros ficam em `%LOCALAPPDATA%\WPSupportTools\Uso\Pendentes\` e são enviados quando o painel estiver
configurado (`TelemetryConstants.Endpoint`/`PublicKey`). **Desde a v0.6.1 vão para o Supabase** (projeto `wp-support-tools`; painel em https://wp-support-tools-painel.vercel.app); os
guardados nos últimos 90 dias também são enviados. Sem internet, tentam de novo na próxima abertura.

**Como o painel conta:** atendimento com `resultado = falhou` → foi para o suporte; `concluido` + retorno
`resolvido = true` → resolvido pelo app; `concluido` + `resolvido = false` → foi para o suporte;
`concluido` sem retorno → sem resposta.

## Internet

- **Interface:** envia os registros de uso acima para o painel (quando configurado). Nada mais.
- **Serviço** (só no PC instalado): consulta e baixa atualizações do GitHub (`latest.json` e o `.exe` do release
  "unidades") — autorizado pelo suporte.
- Logs nunca são enviados.

## Instalação nas unidades e atualização automática

Guia completo: **[docs/instalacao-e-atualizacao.md](docs/instalacao-e-atualizacao.md)**.

- O técnico instala uma vez por PC ("Instalar neste computador", no rodapé): programa em Program Files,
  atalho e o **serviço do Windows**, que faz as etapas de administrador **sem pedir senha**.
- Versões de teste: https://github.com/Leozin7xs/Leozin7xs/releases/latest
- As unidades só recebem o que for aprovado no botão **Actions → "Publicar para as unidades"**,
  assinado com a chave da WP; o serviço confere a assinatura e se atualiza sozinho (a cada 6 h).
- Sem o serviço instalado, o app continua funcionando e pede a permissão pelo UAC, como antes.

## Compilar, testar e gerar o .exe (desenvolvimento)

Pré-requisito (somente na máquina de desenvolvimento): [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet test                                  # testes automatizados
dotnet run --project src\WPSupportTools.App  # abrir o app (Windows)
powershell -File build\publish.ps1           # gera publish\win-x64\WPSupportTools.exe
```

O `.exe` gerado é *self-contained* (≈62 MB) e roda em Windows 10/11 x64 sem instalar o .NET.
O GitHub também gera o `.exe` automaticamente a cada envio (`.github/workflows/build.yml`).

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
