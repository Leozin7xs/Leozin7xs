# Instalação nas unidades e atualização automática

## Como funciona

```
Você testa a versão nova  ──►  botão "Publicar para as unidades"  ──►  unidades se atualizam sozinhas (até 6 h)
   (releases/latest)              (assina com a chave da WP)              (serviço confere a assinatura)
```

- **Instalado em cada PC (uma vez, pelo técnico):** o programa em `C:\Program Files\WP Support Tools`,
  atalho na área de trabalho e o **serviço "WP Support Tools"**.
- **O serviço** faz as etapas de administrador (limpar fila, reiniciar spooler) **sem pedir senha** e
  verifica atualizações ao iniciar e a cada 6 horas.
- **Só vai para as unidades o que você publicar** pelo botão. As versões geradas a cada mudança no código
  ficam em `releases/latest` só para teste.
- **Internet:** o serviço apenas consulta e baixa do GitHub. Não envia log, nome do PC nem dados.
- **Segurança:** a atualização só é instalada se a assinatura conferir com a chave da WP embutida no
  programa, e se o arquivo baixado bater com o tamanho, o SHA-256 e a versão publicados.
  **Ative a verificação em duas etapas na conta do GitHub** — quem controla a conta controla as atualizações.

## 1. Configurar a chave de atualização (uma vez só)

Precisa ser feito **antes** de instalar nas unidades: só as versões geradas depois disso conseguem
se atualizar sozinhas.

1. No seu PC, abra o **Windows PowerShell** e cole:

   ```powershell
   $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider 3072
   $rsa.PersistKeyInCsp = $false
   Set-Clipboard -Value $rsa.ToXmlString($true)
   "Chave copiada. Cole no GitHub agora (não mostre nem envie para ninguém)."
   ```

   A chave vai para a área de transferência, sem aparecer na tela.
2. No GitHub: repositório → **Settings** → **Secrets and variables** → **Actions** →
   **New repository secret**. Nome: `WPST_UPDATE_KEY`. Valor: cole (Ctrl+V). **Add secret**.
3. **Guarde uma cópia** da chave num cofre de senhas da TI (cole lá também). Se ela se perder, as
   unidades já instaladas não aceitam versões assinadas com uma chave nova e precisam ser reinstaladas.
4. Limpe a área de transferência: `Set-Clipboard -Value " "`.
5. Gere a versão de novo com a chave: aba **Actions** → **Gerar WPSupportTools.exe** → **Run workflow**.

> Nunca cole essa chave em chat, e-mail ou arquivo do repositório. O repositório é público.

## 2. Instalar num computador (técnico, uma vez por PC)

1. Baixe o `.exe` de teste aprovado: https://github.com/Leozin7xs/Leozin7xs/releases/latest
   (ou do release **unidades**, depois da primeira publicação).
2. Abra o `.exe` (pelo LDesk/Hashdesk, se for remoto) e clique em **"Instalar neste computador"**,
   no rodapé.
3. Digite a senha de administrador **uma vez** quando o Windows pedir.
4. Pronto: atalho na área de trabalho, serviço rodando. O rodapé passa a mostrar
   "instalado, atualização automática".

Para reinstalar/atualizar a instalação à mão, repita os passos com um `.exe` mais novo.
Log da instalação e do serviço: `C:\ProgramData\WPSupportTools\Logs\` (`instalacao-*.log`, `servico-*.log`).

**Remover:** abra o PowerShell como administrador e rode
`& "C:\Program Files\WP Support Tools\WPSupportTools.exe" --uninstall` (remove o serviço e os atalhos;
a pasta pode ser apagada depois).

## 3. Publicar uma versão para as unidades

1. Teste a versão nova no seu PC (`releases/latest`).
2. Aba **Actions** → **Publicar para as unidades** → **Run workflow** → (deixe a versão em branco para a
   atual, ou digite a testada, ex.: `0.5.0`) → **Run workflow**.
3. O workflow pega **exatamente o mesmo `.exe` que você testou**, assina e publica no release
   **unidades**. As unidades instalam em até 6 horas, sempre fora de uma manutenção em andamento.

Se aparecer "gerada com outra chave (ou sem chave)", gere a versão de novo (passo 1.5), teste e publique.
