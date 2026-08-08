{{ENV}}
{{INSTALL}}
touch /stage/.baked
tar czf /out/deps.tar.gz -C /stage .
touch /out/.done
