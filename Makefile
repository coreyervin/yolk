PREFIX ?= $(HOME)/.local
BIN := yolk

$(BIN): main.swift
	swiftc -O -o $(BIN) main.swift

install: $(BIN)
	install -d $(PREFIX)/bin
	install -m 755 $(BIN) $(PREFIX)/bin/$(BIN)
	@echo "installed $(PREFIX)/bin/$(BIN)"

uninstall:
	rm -f $(PREFIX)/bin/$(BIN)

clean:
	rm -f $(BIN)

.PHONY: install uninstall clean
