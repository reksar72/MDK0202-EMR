# Настройка main после публикации в GitHub

Локальная проверка не включает серверную защиту GitHub. Её нужно включить в Settings → Branches → Add classic branch protection rule для main.

Настройки: Require a pull request before merging; одно независимое одобрение в командном режиме; Dismiss stale approvals; Require conversation resolution; Require status checks (build-and-test); Require branches to be up to date; запрет force push и удаления. После первого запуска Actions выбрать существующий check build-and-test.

Для Git Flow НЕ включать Require linear history: модель использует merge-коммиты --no-ff. Автор одиночного репозитория не может одобрить собственный PR; для командного задания нужен второй reviewer с Write. Не создавать фиктивное одобрение.

GitHub Free предоставляет protected branches для публичных репозиториев. Для приватного репозитория возможность зависит от тарифа. Публично размещать только учебные материалы без реальных пациентов и секретов.
