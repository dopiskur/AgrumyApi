chmod -R 755 /home/api/public_html

nano /etc/systemd/system/kestrel-agrumy.service

-----------
[Unit]
    Description=agrumy

    [Service]
    WorkingDirectory=/home/api/public_html
    ExecStart=/home/api/public_html/agrumy
    Restart=always
    RestartSec=10    # Restart service after 10 seconds if dotnet service crashes
    SyslogIdentifier=dotnet-agrumy
    User=www-data
    Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
-----------
chhmod - R 755 /home/api/public_html/agrumy
-----------
systemctl enable kestrel-agrumy.service 
systemctl start kestrel-agrumy.service  
systemctl status kestrel-agrumy.service 
systemctl stop kestrel-agrumy.service
systemctl restart kestrel-agrumy.service
