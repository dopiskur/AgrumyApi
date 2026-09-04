chmod -R 755 /home/api/public_html

nano /etc/systemd/system/agrumy-api.service

-----------
[Unit]
Description=agrumy

[Service]
WorkingDirectory=/home/apiagrumy/bin
ExecStart=/home/apiagrumy/bin/Agrumy.Api
Restart=always
RestartSec=10
SyslogIdentifier=dotnet-agrumy
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/home/apiagrumy/bin/.extract

[Install]
WantedBy=multi-user.target

-----------

nano /etc/systemd/system/agrumy-web.service

-----------
[Unit]
Description=agrumy Web

[Service]
WorkingDirectory=/home/adminagrumy/bin
ExecStart=/home/adminagrumy/bin/Agrumy.Web
Restart=always
RestartSec=10
SyslogIdentifier=dotnet-agrumy-web
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/home/adminagrumy/bin/.extract

[Install]
WantedBy=multi-user.target




-----------
chhmod - R 755 /home/api/public_html/agrumy
-----------
systemctl enable agrumy-api.service
systemctl enable agrumy-web.service

systemctl start agrumy-api.service
systemctl start agrumy-web.service

systemctl stop agrumy-api.service
systemctl stop agrumy-web.service

systemctl restart agrumy-api.service
systemctl restart agrumy-web.service

systemctl status agrumy-api.service
systemctl status agrumy-web.service
