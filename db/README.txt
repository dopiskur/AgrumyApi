chmod -R 755 /home/api/public_html

nano /etc/systemd/system/kestrel-agrumy.service

-----------
[Unit]
Description=agrumy

[Service]
WorkingDirectory=/home/apiagrumy/bin
ExecStart=/home/apiagrumy/bin/Agrumy.Api
Restart=always
RestartSec=10    # Restart service after 10 seconds if dotnet service crashes
SyslogIdentifier=dotnet-agrumy
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/home/apiagrumy/bin/.extract

[Install]
WantedBy=multi-user.target

-----------

nano /etc/systemd/system/kestrel-agrumyweb.service

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
systemctl enable kestrel-agrumy.service 
systemctl enable kestrel-agrumyweb.service 

systemctl start kestrel-agrumy.service 
systemctl start kestrel-agrumyweb.service 

systemctl stop kestrel-agrumy.service
systemctl stop kestrel-agrumyweb.service

systemctl restart kestrel-agrumy.service
systemctl restart kestrel-agrumyweb.service

systemctl status kestrel-agrumy.service 
systemctl status kestrel-agrumyweb.service
